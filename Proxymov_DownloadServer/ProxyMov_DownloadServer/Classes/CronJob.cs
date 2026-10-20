using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using PuppeteerSharp;
using Quartz;

namespace ProxyMov_DownloadServer.Classes;

internal class CronJob(
    ILogger<CronJob> logger,
    IApiService apiService,
    IConverterService converterService,
    IHostApplicationLifetime appLifetime,
    IQuartzService quartzService,
    DownloadRuntimeState runtimeState,
    IStreamingPortalServiceFactory streamingPortalServiceFactory) : IJob
{
    // Portal requests and the browser based stream lookup run one at a time, only the ffmpeg downloads run in parallel.
    private static readonly SemaphoreSlim ResolveLock = new(1, 1);
    private static int PendingLanguageDownloads;

    private int ActiveWorkers;

    private IBrowser? Browser;
    private bool RegisteredShutdown { get; set; }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        if (!RegisteredShutdown)
        {
            appLifetime.ApplicationStopping.Register(async () => { await Abort(); });

            RegisteredShutdown = true;
        }

        runtimeState.NextRun = context!.NextFireTimeUtc!.Value.ToLocalTime().DateTime;
        await CheckForNewDownloads(cancellationToken);
    }

    private void SetCronJobState(CronJobState jobState)
    {
        runtimeState.SetState(jobState);
        logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | {InfoMessage.CronJobChangedState} {jobState}");
    }

    private void SetCronJobDownloads(int downloadCount, int languageDownloadCount)
    {
        runtimeState.SetDownloadCounts(downloadCount, languageDownloadCount);
    }

    public async Task CheckForNewDownloads(CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | {runtimeState.CronJobState}");

        if (runtimeState.CronJobState != CronJobState.WaitForNextCycle) return;

        SettingsModel? settings = SettingsHelper.ReadSettings<SettingsModel>();

        if (settings is null || string.IsNullOrEmpty(settings.DownloadPath) || string.IsNullOrEmpty(settings.ApiUrl))
        {
            logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | {ErrorMessage.ReadSettings}");
            runtimeState.RaiseError(MessageType.Error, ErrorMessage.ReadSettings);
            return;
        }

        if (settings.ConverterSettings is null)
        {
            settings.ConverterSettings = new ConverterSettingsModel();
            SettingsHelper.SaveSettings(settings);
        }

        SetCronJobState(CronJobState.CheckingForDownloads);
        runtimeState.SetPendingCaptchaUrl(null);

        DownloaderPreferencesModel? downloaderPreferences =
            await apiService.GetAsync<DownloaderPreferencesModel?>("getDownloaderPreferences") ?? new();

        runtimeState.ClearSkippedDownloads();

        IEnumerable<EpisodeDownloadModel>? downloads =
            await apiService.GetAsync<IEnumerable<EpisodeDownloadModel>?>("getDownloads");

        if (downloads is null || !downloads.Any())
        {
            SetCronJobDownloads(0, 0);
            SetCronJobState(CronJobState.WaitForNextCycle);

            runtimeState.RaiseError(MessageType.Info, InfoMessage.NoDownloadsInQueue);
            return;
        }

        SetCronJobState(CronJobState.Running);

        runtimeState.SetDownloadQueue(downloads.EnqueueRange());
        ConverterService.CTS = new CancellationTokenSource();
        Interlocked.Exchange(ref PendingLanguageDownloads, 0);

        runtimeState.SetMaxConcurrentDownloads(settings.MaxConcurrentDownloads);

        await RunDownloadWorkers(settings, downloaderPreferences, cancellationToken);

        runtimeState.SetDownloadQueue(null);
        SetCronJobDownloads(0, 0);
        SetCronJobState(CronJobState.WaitForNextCycle);
    }

    // Keeps as many workers running as configured. A changed setting is applied while the queue is running:
    // additional workers start right away, surplus workers stop after their current episode.
    private async Task RunDownloadWorkers(SettingsModel settings, DownloaderPreferencesModel downloaderPreferences,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource stopQueueCTS = new();
        using SemaphoreSlim maxConcurrentDownloadsChanged = new(0);

        void OnMaxConcurrentDownloadsChanged(int _) => maxConcurrentDownloadsChanged.Release();

        runtimeState.MaxConcurrentDownloadsChanged += OnMaxConcurrentDownloadsChanged;

        List<Task> workers = [];
        Task settingChangedTask = maxConcurrentDownloadsChanged.WaitAsync(cancellationToken);
        ActiveWorkers = 0;

        try
        {
            while (true)
            {
                while (Volatile.Read(ref ActiveWorkers) < runtimeState.MaxConcurrentDownloads &&
                       !stopQueueCTS.IsCancellationRequested &&
                       ConverterService.CTS is not { IsCancellationRequested: true } &&
                       runtimeState.GetQueuedDownloadCount() > 0)
                {
                    Interlocked.Increment(ref ActiveWorkers);
                    workers.Add(ProcessDownloadQueue(settings, downloaderPreferences, stopQueueCTS, cancellationToken));
                }

                if (workers.Count == 0) break;

                Task finishedTask = await Task.WhenAny([.. workers, settingChangedTask]);

                if (finishedTask == settingChangedTask)
                {
                    await settingChangedTask;
                    settingChangedTask = maxConcurrentDownloadsChanged.WaitAsync(cancellationToken);
                    continue;
                }

                workers.Remove(finishedTask);
                await finishedTask;
            }
        }
        finally
        {
            runtimeState.MaxConcurrentDownloadsChanged -= OnMaxConcurrentDownloadsChanged;
        }
    }

    private async Task ProcessDownloadQueue(SettingsModel settings, DownloaderPreferencesModel downloaderPreferences,
        CancellationTokenSource stopQueueCTS, CancellationToken cancellationToken)
    {
        bool retired = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (stopQueueCTS.IsCancellationRequested) break;

                if (ConverterService.CTS is not null && ConverterService.CTS.IsCancellationRequested) break;

                int workerCount = Volatile.Read(ref ActiveWorkers);

                if (workerCount > runtimeState.MaxConcurrentDownloads)
                {
                    if (Interlocked.CompareExchange(ref ActiveWorkers, workerCount - 1, workerCount) == workerCount)
                    {
                        retired = true;
                        break;
                    }

                    continue;
                }

                if (!runtimeState.TryDequeueDownload(out EpisodeDownloadModel? episode, out int remainingCount)) break;

                if (runtimeState.SkippedDownloads.Contains(episode!)) continue;

                SetCronJobDownloads(remainingCount, Volatile.Read(ref PendingLanguageDownloads));

                bool continueQueue = await ProcessEpisode(episode!, settings, downloaderPreferences);

                if (!continueQueue)
                {
                    stopQueueCTS.Cancel();
                    break;
                }
            }
        }
        finally
        {
            if (!retired) Interlocked.Decrement(ref ActiveWorkers);
        }
    }

    // Returns false when the whole queue has to stop (captcha, cancel, stop mark).
    private async Task<bool> ProcessEpisode(EpisodeDownloadModel episode, SettingsModel settings,
        DownloaderPreferencesModel downloaderPreferences)
    {
        string? logMessage;

        if (string.IsNullOrEmpty(episode.Download.Name)) return true;

        string? originalEpisodeName = episode.Download.Name;

        if (!Enum.TryParse(episode.StreamingPortal.Name, true, out StreamingPortal streamingPortal))
            return true;

        List<DirectViewLinkModel>? directViewLinks;
        bool hasError = false;
        string hosterEpisodeUrl;

        await ResolveLock.WaitAsync();

        try
        {
            IStreamingPortalService streamingPortalService = streamingPortalServiceFactory.GetService(streamingPortal);
            directViewLinks = await streamingPortalService.GetDirectViewLinksAsync(episode);
            hosterEpisodeUrl = streamingPortalService.GetHosterEpisodeUrl(episode);
        }
        catch (HttpRequestException ex)
        {
            runtimeState.RaiseError(MessageType.Error, ex.Message);
            logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | {ex.Message}");

            hasError = true;
            return true;
        }
        catch (Exception ex)
        {
            runtimeState.RaiseError(MessageType.Error, ex.Message);
            logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | {ex.Message}");

            hasError = true;
            return true;
        }
        finally
        {
            ResolveLock.Release();

            if (hasError) runtimeState.RaiseError(MessageType.Warning, WarningMessage.DownloadNotRemoved);
        }

        if (directViewLinks == null || directViewLinks.Count == 0) return true;

        IEnumerable<Language> episodeLanguages =
            episode.Download.LanguageFlag.GetFlags<Language>(Language.None);

        List<DirectViewLinkModel> selectedDirectViewLinks =
        [
            .. directViewLinks.Where(_ => episodeLanguages.Contains(_.Language))
        ];

        int finishedDownloadsCount = 1;
        int startedDownloadsCount = 0;

        Interlocked.Add(ref PendingLanguageDownloads, selectedDirectViewLinks.Count);

        try
        {
            foreach (DirectViewLinkModel directViewLink in selectedDirectViewLinks)
            {
                startedDownloadsCount++;
                Interlocked.Decrement(ref PendingLanguageDownloads);
                SetCronJobDownloads(runtimeState.GetQueuedDownloadCount(), Volatile.Read(ref PendingLanguageDownloads));

                // Another worker ran into a captcha, don't hit the hoster again.
                if (runtimeState.PendingCaptchaUrl is not null) return false;

                bool solveCaptcha;

                await ResolveLock.WaitAsync();

                try
                {
                    (solveCaptcha, episode.M3U8Url) = await GetEpisodeM3U8(hosterEpisodeUrl, directViewLink.DirectLink, downloaderPreferences);
                }
                finally
                {
                    ResolveLock.Release();
                }

                if (solveCaptcha)
                {
                    runtimeState.SetPendingCaptchaUrl(hosterEpisodeUrl);
                    runtimeState.RaiseError(MessageType.Error, InfoMessage.CaptchaDetected);
                    await NotifyCaptchaIfEnabled(downloaderPreferences, hosterEpisodeUrl, streamingPortal);

                    // Running downloads of other workers are allowed to finish.
                    runtimeState.NextRun = null;
                    quartzService.CancelJob();
                    runtimeState.StopMarkDownload = null;
                    SetCronJobState(CronJobState.Captcha);
                    return false;
                }

                if (string.IsNullOrEmpty(episode.M3U8Url))
                {
                    logMessage =
                        $"Für \"{originalEpisodeName} | S{episode.Download.Season:D2} E{episode.Download.Episode:D2}\" wurde keine Video Source gefunden.";
                    runtimeState.RaiseError(MessageType.Secondary, logMessage);
                    continue;
                }

                logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | Stream Url: {episode.M3U8Url}");

                episode.Download.Name = originalEpisodeName;

                CommandResultExt? result = await converterService.StartDownload(
                    episode,
                    settings.DownloadPath!,
                    downloaderPreferences,
                    settings.ConverterSettings,
                    directViewLink.Language);

                finishedDownloadsCount++;

                if (result is not null && result.Skipped)
                {
                    runtimeState.RaiseError(MessageType.Secondary, InfoMessage.EpisodeDownloadSkipped);

                    continue;
                }

                if (result is not null && result.SkippedNoResult)
                {
                    runtimeState.RaiseError(MessageType.Secondary, InfoMessage.EpisodeDownloadSkippedFileExists);

                    await RemoveDownload(episode);

                    continue;
                }

                if (ConverterService.CTS is not null && (result is null || !result.IsSuccess))
                {
                    if (ConverterService.CTS.IsCancellationRequested)
                    {
                        logMessage = $"{WarningMessage.DownloadCanceled} {WarningMessage.DownloadNotRemoved}";
                        runtimeState.RaiseError(MessageType.Warning, logMessage);
                        return false;
                    }

                    logMessage = $"{WarningMessage.FFMPEGExecutableFail}\n{WarningMessage.DownloadNotRemoved}";
                    runtimeState.RaiseError(MessageType.Warning, logMessage);
                }

                if (result is not null && result.IsSuccess)
                {
                    runtimeState.RaiseError(MessageType.Success, InfoMessage.DownloadFinished);

                    if (finishedDownloadsCount >= selectedDirectViewLinks.Count)
                    {
                        await RemoveDownload(episode);
                    }
                }
            }
        }
        finally
        {
            Interlocked.Add(ref PendingLanguageDownloads, startedDownloadsCount - selectedDirectViewLinks.Count);
        }

        if (runtimeState.StopMarkDownload is not null && runtimeState.StopMarkDownload.Download == episode.Download)
        {
            // Running downloads of other workers are allowed to finish, no new ones are started.
            runtimeState.NextRun = null;
            quartzService.CancelJob();
            runtimeState.StopMarkDownload = null;
            runtimeState.RaiseError(MessageType.Info, InfoMessage.StopMarkReached);
            SetCronJobState(CronJobState.Paused);
            return false;
        }

        return true;
    }

    public async Task Abort()
    {
        if (Browser is not null) await Browser.CloseAsync();

        ConverterService.Abort();
        runtimeState.NextRun = null;
    }

    private async Task<(bool captcha, string? )> GetEpisodeM3U8(string episodeUrl, string streamUrl, DownloaderPreferencesModel downloaderPreferences)
    {
        Browser ??= await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = Helper.GetBrowserBinPath(),
            Args =
            [
                "--no-sandbox", downloaderPreferences.UseProxy ? $"--proxy-server={downloaderPreferences.ProxyUri}" : ""
            ]
        });

        string proxyLogText = $"| Url: {downloaderPreferences.ProxyUri}@{downloaderPreferences.ProxyUsername}";
        logger.LogInformation(
            $"{DateTime.UtcNow.ToLocalTime()} | Use Proxy: {downloaderPreferences.UseProxy} {(downloaderPreferences.UseProxy ? proxyLogText : "")}");

        using IPage? page = await Browser.NewPageAsync();

        try
        {
            string? videoPageHtml = await GetVideoPageHtml(page, episodeUrl);
            bool captcha = await HasCloudflareCaptcha(page, TimeSpan.FromSeconds(10));

            videoPageHtml = await GetVideoPageHtml(page, streamUrl);

            if (string.IsNullOrEmpty(videoPageHtml)) return (captcha, null);

            if (TryGetVideoSource(videoPageHtml, out string? m3u8)) return (captcha, m3u8);

            string? m3u8FromJwPlayer = await GetM3U8ViaJwPlayer(page);

            if (!string.IsNullOrEmpty(m3u8FromJwPlayer))
            {
                return (captcha, m3u8FromJwPlayer);
            }

            return (captcha, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            return (false, null);
        }
        finally
        {
            await Browser.CloseAsync();
            Browser = null;
        }
    }

    private static bool TryGetVideoSource(string html, out string? m3u8)
    {
        m3u8 = null;

        HtmlDocument htmlDocument = new();
        htmlDocument.LoadHtml(html);

        Regex regex = new("https://delivery-node-(.*?)\\);");
        Match m3u8NodeMatch = regex.Match(html);

        if (m3u8NodeMatch.Success)
        {
            m3u8 = HttpUtility.HtmlDecode(m3u8NodeMatch.Value.TrimEnd('"', ')', ';'));
            return true;
        }

        Match hlsMatch = new Regex("'hls': '(.*?)',").Match(html);

        if (hlsMatch.Success)
        {
            m3u8 = HttpUtility.HtmlDecode(hlsMatch.Groups[1].Value);
            return true;
        }

        Match sourceMatch =
            new Regex("<source src=\"(.*?)\" type=\"application/x-mpegurl\" data-vds=\"\">").Match(html);

        if (sourceMatch.Success)
        {
            m3u8 = HttpUtility.HtmlDecode(sourceMatch.Groups[1].Value);
            return true;
        }

        Match playerSourceMatch = new Regex("<source src=\"(.*?)\"").Match(html);

        if (playerSourceMatch.Success)
        {
            m3u8 = HttpUtility.HtmlDecode(playerSourceMatch.Groups[1].Value);
            return true;
        }

        return false;
    }

    private async Task<string?> GetVideoPageHtml(IPage page, string url)
    {
        logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | Navigation to Page {url}");

        await page.GoToAsync(url, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.Networkidle2],
            Timeout = 10000
        });

        return await page.GetContentAsync();
    }

    private static async Task<bool> HasCloudflareCaptcha(IPage page, TimeSpan timeout)
    {
        try
        {
            await page.WaitForFunctionAsync(
                @"() => Boolean(
                    document.querySelector('iframe[src*=""challenges.cloudflare.com""]') ||
                    document.querySelector('iframe[src*=""turnstile""]') ||
                    document.querySelector('.cf-turnstile') ||
                    document.querySelector('[data-sitekey]') ||
                    document.querySelector('input[name=""cf-turnstile-response""]')
                )",
                new WaitForFunctionOptions
                {
                    Timeout = (int)timeout.TotalMilliseconds
                });

            return true;
        }
        catch (WaitTaskTimeoutException)
        {
            return false;
        }
    }

    private async Task<string?> GetM3U8ViaJwPlayer(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync(@"() => {
                    const jw = window.jwplayer;
                    if (typeof jw !== 'function') {
                        return false;
                    }

                    const player = jw() || jw('a');
                    return Boolean(player && typeof player.getPlaylist === 'function');
                }", new WaitForFunctionOptions { Timeout = 10000 });

            await page.EvaluateFunctionAsync(@"() => {
                    const jw = window.jwplayer;
                    if (typeof jw !== 'function') {
                        return;
                    }

                    const player = jw() || jw('a');
                    if (player && typeof player.play === 'function') {
                        player.play();
                    }
                }");

            await Task.Delay(2000);

            string? m3u8Url = await page.EvaluateFunctionAsync<string?>(@"() => {
                    try {
                        const jw = window.jwplayer;
                        if (typeof jw !== 'function') return null;

                        const player = jw() || jw('a');
                        if (!player) return null;
                        
                        const playlist = player.getPlaylist();
                        if (playlist && playlist.length > 0) {
                            const sources = playlist[0].sources;
                            if (sources) {
                                for (const source of sources) {
                                    if (source.file && (source.file.includes('.m3u8') || source.type === 'hls')) {
                                        return source.file;
                                    }
                                }
                            }
                        }                        
                        
                        const currentItem = player.getPlaylistItem();
                        if (currentItem && currentItem.sources) {
                            for (const source of currentItem.sources) {
                                if (source.file && source.file.includes('.m3u8')) {
                                    return source.file;
                                }
                            }
                            if (currentItem.file) {
                                return currentItem.file;
                            }
                        }                        
                        
                        const config = player.getConfig();
                        if (config && config.playlist && config.playlist[0]) {
                            const src = config.playlist[0].sources;
                            if (src && src[0] && src[0].file) {
                                return src[0].file;
                            }
                        }
                        
                        return null;
                    } catch (e) {
                        return null;
                    }
                }");

            return m3u8Url;
        }
        catch (Exception ex)
        {
            logger.LogWarning($"JW Player Extraktion fehlgeschlagen: {ex.Message}");
            return null;
        }
    }

    private async Task RemoveDownload(EpisodeDownloadModel episodeDownload)
    {
        bool removeSuccess = await apiService.RemoveFinishedDownload(episodeDownload);

        if (!removeSuccess) runtimeState.RaiseError(MessageType.Warning, WarningMessage.DownloadNotRemoved);
    }

    private async Task NotifyCaptchaIfEnabled(DownloaderPreferencesModel downloaderPreferences, string hosterEpisodeUrl,
        StreamingPortal streamingPortal)
    {
        if (!downloaderPreferences.TelegramCaptchaNotification) return;

        try
        {
            string host = hosterEpisodeUrl;
            if (Uri.TryCreate(hosterEpisodeUrl, UriKind.Absolute, out Uri? uri))
            {
                host = uri.Host;
            }

            HosterModel hosterModel = new(host, streamingPortal, hosterEpisodeUrl);
            await apiService.SendCaptchaNotification(hosterModel);
        }
        catch (Exception ex)
        {
            logger.LogWarning($"{DateTime.UtcNow.ToLocalTime()} | Telegram captcha notification failed: {ex.Message}");
        }
    }
}
