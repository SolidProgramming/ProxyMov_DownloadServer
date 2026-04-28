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
    private IBrowser? Browser;
    private IPage? CurrentPage;
    private bool RegisteredShutdown { get; set; }

    public async Task Execute(IJobExecutionContext context)
    {
        if (!RegisteredShutdown)
        {
            appLifetime.ApplicationStopping.Register(async () => { await Abort(); });

            RegisteredShutdown = true;
        }

        runtimeState.NextRun = context!.NextFireTimeUtc!.Value.ToLocalTime().DateTime;
        await CheckForNewDownloads();
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

    public async Task CheckForNewDownloads()
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

        DownloaderPreferencesModel? downloaderPreferences =
            await apiService.GetAsync<DownloaderPreferencesModel?>("getDownloaderPreferences") ?? new();
        string? logMessage;

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

        while (runtimeState.DownloadQueue!.Count != 0)
        {
            if (ConverterService.CTS is not null && ConverterService.CTS.IsCancellationRequested &&
                !ConverterService.AbortIsSkip)
                break;

            EpisodeDownloadModel episode = runtimeState.DownloadQueue.Dequeue();

            if (runtimeState.SkippedDownloads.Contains(episode)) continue;

            SetCronJobDownloads(runtimeState.DownloadQueue.Count, 0);

            if (string.IsNullOrEmpty(episode.Download.Name)) continue;

            string? originalEpisodeName = episode.Download.Name;

            if (!Enum.TryParse(episode.StreamingPortal.Name, true, out StreamingPortal streamingPortal))
                continue;

            List<DirectViewLinkModel>? directViewLinks;
            bool hasError = false;

            try
            {
                IStreamingPortalService streamingPortalService = streamingPortalServiceFactory.GetService(streamingPortal);
                directViewLinks = await streamingPortalService.GetDirectViewLinksAsync(episode);
            }
            catch (HttpRequestException ex)
            {
                runtimeState.RaiseError(MessageType.Error, ex.Message);
                logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | {ex.Message}");

                hasError = true;
                continue;
            }
            catch (Exception ex)
            {
                runtimeState.RaiseError(MessageType.Error, ex.Message);
                logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | {ex.Message}");

                hasError = true;
                continue;
            }
            finally
            {
                if (hasError) runtimeState.RaiseError(MessageType.Warning, WarningMessage.DownloadNotRemoved);
            }

            if (directViewLinks == null || directViewLinks.Count == 0) continue;

            IEnumerable<Language> episodeLanguages =
                episode.Download.LanguageFlag.GetFlags<Language>(Language.None);

            List<DirectViewLinkModel> selectedDirectViewLinks =
            [
                .. directViewLinks.Where(_ => episodeLanguages.Contains(_.Language))
            ];

            int finishedDownloadsCount = 1;

            foreach (DirectViewLinkModel directViewLink in selectedDirectViewLinks)
            {
                SetCronJobDownloads(runtimeState.DownloadQueue.Count, selectedDirectViewLinks.Count - finishedDownloadsCount);

                episode.M3U8Url = await GetEpisodeM3U8(directViewLink.DirectLink, downloaderPreferences, ConverterService.CTS?.Token ?? CancellationToken.None);

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
                    settings.DownloadPath,
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
                        break;
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

            if (runtimeState.StopMarkDownload is not null && runtimeState.StopMarkDownload.Download == episode.Download)
            {
                await Abort();
                quartzService.CancelJob();
                runtimeState.StopMarkDownload = null;
                runtimeState.RaiseError(MessageType.Info, InfoMessage.StopMarkReached);
                SetCronJobState(CronJobState.Paused);
                break;
            }
        }

        runtimeState.SetDownloadQueue(null);
        SetCronJobDownloads(0, 0);
        SetCronJobState(CronJobState.WaitForNextCycle);
    }

    public async Task Abort()
    {
        if (CurrentPage is not null)
        {
            await CurrentPage.CloseAsync();
            CurrentPage = null;
        }

        if (Browser is not null)
        {
            await Browser.CloseAsync();
            Browser = null;
        }

        ConverterService.Abort();
        runtimeState.NextRun = null;
    }

    private async Task<string?> GetEpisodeM3U8(string streamUrl, DownloaderPreferencesModel downloaderPreferences, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        cancellationToken.ThrowIfCancellationRequested();
        CurrentPage = await Browser.NewPageAsync();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? videoPageHtml = await GetVideoPageHtml(CurrentPage, streamUrl, cancellationToken);

            if (string.IsNullOrEmpty(videoPageHtml)) return null;

            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetVideoSource(videoPageHtml, out string? m3u8)) return m3u8;

            cancellationToken.ThrowIfCancellationRequested();
            string? m3u8FromJwPlayer = await GetM3U8ViaJwPlayer(CurrentPage, cancellationToken);

            if (!string.IsNullOrEmpty(m3u8FromJwPlayer))
            {
                return m3u8FromJwPlayer;
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            return null;
        }
        finally
        {
            if (CurrentPage is not null)
            {
                await CurrentPage.CloseAsync();
                CurrentPage = null;
            }

            if (Browser is not null)
            {
                await Browser.CloseAsync();
                Browser = null;
            }
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

    private async Task<string?> GetVideoPageHtml(IPage page, string streamUrl, CancellationToken cancellationToken)
    {
        logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | Navigation to Page {streamUrl}");

        cancellationToken.ThrowIfCancellationRequested();
        await page.GoToAsync(streamUrl, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
            Timeout = 10000
        });
        cancellationToken.ThrowIfCancellationRequested();

        return await page.GetContentAsync();
    }

    private async Task<string?> GetM3U8ViaJwPlayer(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await page.WaitForFunctionAsync(@"() => {
                    return typeof jwplayer !== 'undefined' && 
                           jwplayer('a') && 
                           typeof jwplayer('a').getPlaylist === 'function';
                }", new WaitForFunctionOptions { Timeout = 10000 });

            cancellationToken.ThrowIfCancellationRequested();
            await page.EvaluateFunctionAsync(@"() => {
                    const player = jwplayer('a');
                    if (player && typeof player.play === 'function') {
                        player.play();
                    }
                }");

            await Task.Delay(2000, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            string? m3u8Url = await page.EvaluateFunctionAsync<string?>(@"() => {
                    try {
                        const player = jwplayer('a');
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
        catch (OperationCanceledException)
        {
            throw;
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
}
