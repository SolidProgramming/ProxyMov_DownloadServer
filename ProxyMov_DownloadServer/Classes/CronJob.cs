using HtmlAgilityPack;
using PuppeteerSharp;
using Quartz;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;


namespace ProxyMov_DownloadServer.Classes
{
    internal class CronJob(ILogger<CronJob> logger, IApiService apiService, IConverterService converterService, IHostApplicationLifetime appLifetime, IQuartzService quartzService)
         : IJob
    {
        public delegate void CronJobEventHandler(CronJobState jobState);
        public static event CronJobEventHandler? CronJobEvent;

        public delegate void CronJobErrorEventHandler(MessageType messageType, string message);
        public static event CronJobErrorEventHandler? CronJobErrorEvent;

        public delegate void CronJobDownloadsEventHandler(int downloadCount, int languageDownloadCount);
        public static event CronJobDownloadsEventHandler? CronJobDownloadsEvent;
        public static CronJobState CronJobState { get; set; } = CronJobState.WaitForNextCycle;

        public static Queue<EpisodeDownloadModel>? DownloadQue;
        public static List<EpisodeDownloadModel> SkippedDownloads = [];

        public static EpisodeDownloadModel? StopMarkDownload;

        public static int Interval;
        public static DateTime? NextRun = default;

        private IBrowser? Browser;

        public static int DownloadCount { get; set; }
        public static int LanguageDownloadCount { get; set; }

        private static HttpClient? HttpClient;

        public static async Task<bool> InitAsync(WebProxy? proxy = null)
        {
            if(proxy is null)
            {
                HttpClient = HttpClientFactory.CreateHttpClient<CronJob>();
            }
            else
            {
                HttpClient = HttpClientFactory.CreateHttpClient<CronJob>(proxy);
            }

            using HttpClient noProxyHttpClient = HttpClientFactory.CreateHttpClient();

            (bool success, string? ipv4) = await HttpClient.GetIPv4();
            (bool noProxySuccess, string? NoProxyIpv4) = await noProxyHttpClient.GetIPv4();

            if(!success || !noProxySuccess)
                return false;

            if(ipv4 == NoProxyIpv4)
                return false;

            return true;
        }

        private bool RegisteredShutdown { get; set; }

        private void SetCronJobState(CronJobState jobState)
        {
            CronJobState = jobState;
            logger.LogInformation($"{DateTime.Now} | {InfoMessage.CronJobChangedState} {jobState}");

            CronJobEvent?.Invoke(jobState);
        }

        private static void SetCronJobDownloads(int downloadCount, int languageDownloadCount)
        {
            DownloadCount = downloadCount;
            LanguageDownloadCount = languageDownloadCount;

            CronJobDownloadsEvent?.Invoke(downloadCount, languageDownloadCount);
        }

        public async Task Execute(IJobExecutionContext context)
        {
            if(!RegisteredShutdown)
            {
                appLifetime.ApplicationStopping.Register(async () =>
                {
                    await Abort();
                });

                RegisteredShutdown = true;
            }

            NextRun = context!.NextFireTimeUtc!.Value.ToLocalTime().DateTime;
            await CheckForNewDownloads();
        }

        public async Task CheckForNewDownloads()
        {
            logger.LogInformation($"{DateTime.Now} | {CronJobState}");

            if(CronJobState != CronJobState.WaitForNextCycle)
                return;

            SettingsModel? settings = SettingsHelper.ReadSettings<SettingsModel>();

            if(settings is null || string.IsNullOrEmpty(settings.DownloadPath) || string.IsNullOrEmpty(settings.ApiUrl))
            {
                logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettings}");
                CronJobErrorEvent?.Invoke(MessageType.Error, ErrorMessage.ReadSettings);
                return;
            }

            if(settings.ConverterSettings is null)
            {
                settings.ConverterSettings = new();
                SettingsHelper.SaveSettings(settings);
            }

            SetCronJobState(CronJobState.CheckingForDownloads);

            DownloaderPreferencesModel? downloaderPreferences = await apiService.GetAsync<DownloaderPreferencesModel?>("getDownloaderPreferences");

            string? logMessage;

            SkippedDownloads.Clear();

            IEnumerable<EpisodeDownloadModel>? downloads = await apiService.GetAsync<IEnumerable<EpisodeDownloadModel>?>("getDownloads");

            if(downloads is null || !downloads.Any())
            {
                SetCronJobDownloads(0, 0);
                SetCronJobState(CronJobState.WaitForNextCycle);

                CronJobErrorEvent?.Invoke(MessageType.Info, InfoMessage.NoDownloadsInQueue);
                return;
            }

            SetCronJobState(CronJobState.Running);

            DownloadQue = downloads.EnqueueRange();
            ConverterService.CTS = new CancellationTokenSource();

            while(DownloadQue!.Count != 0)
            {
                if(ConverterService.CTS is not null && ConverterService.CTS.IsCancellationRequested && !ConverterService.AbortIsSkip)
                    break;

                EpisodeDownloadModel episodeDownload = DownloadQue.Dequeue();

                if(SkippedDownloads.Contains(episodeDownload))
                    continue;

                SetCronJobDownloads(DownloadQue.Count, 0);

                if(string.IsNullOrEmpty(episodeDownload.Download.Name))
                    continue;

                string originalEpisodeName = episodeDownload.Download.Name;

                string url = "";

                if(episodeDownload.StreamingPortal.Name == Hoster.STO.ToString())
                {
                    url = $"https://s.to/serie/stream{episodeDownload.Download.Path}/{string.Format(Globals.LinkBlueprint, episodeDownload.Download.Season, episodeDownload.Download.Episode)}";
                }
                else if(episodeDownload.StreamingPortal.Name == Hoster.AniWorld.ToString())
                {
                    url = $"https://aniworld.to/anime/stream{episodeDownload.Download.Path}/{string.Format(Globals.LinkBlueprint, episodeDownload.Download.Season, episodeDownload.Download.Episode)}";
                }
                else { continue; }

                string? html;
                bool hasError = false;

                try
                {
                    html = await HttpClient.GetStringAsync(url);
                }
                catch(HttpRequestException ex)
                {
                    CronJobErrorEvent?.Invoke(MessageType.Error, ex.Message);
                    logger.LogError($"{DateTime.Now} | {ex.Message}");

                    hasError = true;
                    continue;
                }
                catch(Exception ex)
                {
                    CronJobErrorEvent?.Invoke(MessageType.Error, ex.Message);
                    logger.LogError($"{DateTime.Now} | {ex.Message}");

                    hasError = true;
                    continue;
                }
                finally
                {
                    if(hasError)
                    {
                        CronJobErrorEvent?.Invoke(MessageType.Warning, WarningMessage.DownloadNotRemoved);
                    }
                }

                Dictionary<Language, List<string>>? languageRedirectLinks = HosterHelper.GetLanguageRedirectLinks(html);

                if(languageRedirectLinks == null || languageRedirectLinks.Count == 0)
                    continue;

                episodeDownload.Download.Name = episodeDownload.Download.Name.GetValidFileName();

                IEnumerable<Language> episodeLanguages = episodeDownload.Download.LanguageFlag.GetFlags<Language>(ignore: Language.None);
                IEnumerable<Language> redirectLanguages = languageRedirectLinks.Keys.Where(lang => episodeLanguages.Contains(lang));

                IEnumerable<Language>? downloadLanguages = episodeLanguages.Intersect(redirectLanguages);

                int finishedDownloadsCount = 1;

                foreach(Language language in downloadLanguages)
                {
                    SetCronJobDownloads(DownloadQue.Count, downloadLanguages.Count() - finishedDownloadsCount);

                    if(episodeDownload.StreamingPortal.Name == Hoster.STO.ToString())
                    {
                        url = $"https://s.to{languageRedirectLinks[language][0]}";
                    }
                    else if(episodeDownload.StreamingPortal.Name == Hoster.AniWorld.ToString())
                    {
                        url = $"https://aniworld.to{languageRedirectLinks[language][0]}";
                    }
                    else { continue; }

                    string? m3u8Url = await GetEpisodeM3U8(url, downloaderPreferences);

                    if(string.IsNullOrEmpty(m3u8Url))
                    {
                        logMessage = $"Für \"{originalEpisodeName} | S{episodeDownload.Download.Season:D2} E{episodeDownload.Download.Episode:D2}\" wurde keine Video Source gefunden.";
                        CronJobErrorEvent?.Invoke(MessageType.Secondary, logMessage);
                        continue;
                    }
                    else
                    {
                        logger.LogInformation($"{DateTime.Now} | Stream Url: {m3u8Url}");
                    }

                    episodeDownload.Download.Name = $"{originalEpisodeName.GetValidFileName()}[{language}]";

                    CommandResultExt? result = await converterService.StartDownload(m3u8Url, episodeDownload.Download, settings.DownloadPath, downloaderPreferences, settings.ConverterSettings);

                    finishedDownloadsCount++;

                    if(result is not null && result.Skipped)
                    {
                        CronJobErrorEvent?.Invoke(MessageType.Secondary, InfoMessage.EpisodeDownloadSkipped);

                        continue;
                    }

                    if(result is not null && result.SkippedNoResult)
                    {
                        CronJobErrorEvent?.Invoke(MessageType.Secondary, InfoMessage.EpisodeDownloadSkippedFileExists);

                        await RemoveDownload(episodeDownload);

                        continue;
                    }

                    if(ConverterService.CTS is not null && (result is null || !result.IsSuccess))
                    {
                        if(ConverterService.CTS.IsCancellationRequested)
                        {
                            logMessage = $"{WarningMessage.DownloadCanceled} {WarningMessage.DownloadNotRemoved}";
                            CronJobErrorEvent?.Invoke(MessageType.Warning, logMessage);
                            break;
                        }
                        else
                        {
                            logMessage = $"{WarningMessage.FFMPEGExecutableFail}\n{WarningMessage.DownloadNotRemoved}";
                            CronJobErrorEvent?.Invoke(MessageType.Warning, logMessage);
                        }
                    }

                    if(result is not null && result.IsSuccess)
                    {
                        CronJobErrorEvent?.Invoke(MessageType.Success, InfoMessage.DownloadFinished);

                        if(finishedDownloadsCount >= downloadLanguages.Count())
                            await RemoveDownload(episodeDownload);
                    }
                }

                if(StopMarkDownload is not null && StopMarkDownload.Download == episodeDownload.Download)
                {
                    await Abort();
                    quartzService.CancelJob();
                    StopMarkDownload = default;
                    CronJobErrorEvent?.Invoke(MessageType.Info, InfoMessage.StopMarkReached);
                    SetCronJobState(CronJobState.Paused);
                    break;
                }
            }

            DownloadQue = default;
            SetCronJobDownloads(0, 0);
            SetCronJobState(CronJobState.WaitForNextCycle);
        }

        public static void RemoveHandlers()
        {
            if(CronJobEvent is not null)
            {
                foreach(Delegate d in CronJobEvent.GetInvocationList())
                {
                    CronJobEvent -= (CronJobEventHandler)d;
                }
            }

            if(CronJobErrorEvent is not null)
            {
                foreach(Delegate d in CronJobErrorEvent.GetInvocationList())
                {
                    CronJobErrorEvent -= (CronJobErrorEventHandler)d;
                }
            }

            if(CronJobDownloadsEvent is not null)
            {
                foreach(Delegate d in CronJobDownloadsEvent.GetInvocationList())
                {
                    CronJobDownloadsEvent -= (CronJobDownloadsEventHandler)d;
                }
            }
        }

        public async Task Abort()
        {
            if(Browser is not null)
            {
                await Browser.CloseAsync();
            }

            ConverterService.Abort();
            NextRun = null;
        }

        private async Task<string?> GetEpisodeM3U8(string streamUrl, DownloaderPreferencesModel downloaderPreferences)
        {
            Browser ??= await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = Helper.GetBrowserBinPath(),
                Args = ["--no-sandbox", (downloaderPreferences.UseProxy ? $"--proxy-server={downloaderPreferences.ProxyUri}" : "")]
            });

            string proxyLogText = $"| Url: {downloaderPreferences.ProxyUri}@{downloaderPreferences.ProxyUsername}";
            logger.LogInformation($"{DateTime.Now} | Use Proxy: {downloaderPreferences.UseProxy} {(downloaderPreferences.UseProxy ? proxyLogText : "")}");

            using IPage? page = await Browser.NewPageAsync();

            try
            {
                string? videoPageHtml = await GetVideoPageHtml(page, streamUrl);

                if (string.IsNullOrEmpty(videoPageHtml))
                    return default;

                if (TryGetVideoSource(videoPageHtml, out string? m3u8))
                    return m3u8;

                string? m3u8FromJwPlayer = await GetM3U8ViaJwPlayer(page);

                if (!string.IsNullOrEmpty(m3u8FromJwPlayer))
                {
                    logger.LogInformation($"{DateTime.Now} | M3U8 aus JW Player API: {m3u8FromJwPlayer}");
                    return m3u8FromJwPlayer;
                }

                return default;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                return null;
            }
            finally
            {
                await Browser.CloseAsync();
                Browser = default;
            }
        }

        private static bool TryGetVideoSource(string html, out string? m3u8)
        {
            m3u8 = default;

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

            Match sourceMatch = new Regex("<source src=\"(.*?)\" type=\"application/x-mpegurl\" data-vds=\"\">").Match(html);

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

        private async Task<string?> GetVideoPageHtml(IPage page, string streamUrl)
        {
            logger.LogInformation($"{DateTime.Now} | Navigation to Page {streamUrl}");

            await page.GoToAsync(streamUrl, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 10000
            });

            return await page.GetContentAsync();
        }

        private async Task<string?> GetM3U8ViaJwPlayer(IPage page)
        {
            try
            {
                await page.WaitForFunctionAsync(@"() => {
                    return typeof jwplayer !== 'undefined' && 
                           jwplayer('a') && 
                           typeof jwplayer('a').getPlaylist === 'function';
                }", new WaitForFunctionOptions { Timeout = 10000 });

                await page.EvaluateFunctionAsync(@"() => {
                    const player = jwplayer('a');
                    if (player && typeof player.play === 'function') {
                        player.play();
                    }
                }");

                //Execute player start and wait for stream to initialize
                await Task.Delay(2000);

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
            catch (Exception ex)
            {
                logger.LogWarning($"JW Player Extraktion fehlgeschlagen: {ex.Message}");
                return null;
            }
        }

        private async Task RemoveDownload(EpisodeDownloadModel episodeDownload)
        {
            bool removeSuccess = await apiService.RemoveFinishedDownload(episodeDownload);

            if(!removeSuccess)
            {
                CronJobErrorEvent?.Invoke(MessageType.Warning, WarningMessage.DownloadNotRemoved);
            }
        }
    }
}
