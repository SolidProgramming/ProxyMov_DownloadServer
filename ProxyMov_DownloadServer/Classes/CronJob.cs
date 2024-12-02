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
            if (proxy is null)
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

            if (!success || !noProxySuccess)
                return false;

            if (ipv4 == NoProxyIpv4)
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
            if (!RegisteredShutdown)
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

            if (CronJobState != CronJobState.WaitForNextCycle)
                return;

            SettingsModel? settings = SettingsHelper.ReadSettings<SettingsModel>();

            if (settings is null || string.IsNullOrEmpty(settings.DownloadPath) || string.IsNullOrEmpty(settings.ApiUrl))
            {
                logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettings}");
                CronJobErrorEvent?.Invoke(MessageType.Error, ErrorMessage.ReadSettings);
                return;
            }

            SetCronJobState(CronJobState.CheckingForDownloads);

            DownloaderPreferencesModel? downloaderPreferences = await apiService.GetAsync<DownloaderPreferencesModel?>("getDownloaderPreferences");

            string? logMessage;

            SkippedDownloads.Clear();

            IEnumerable<EpisodeDownloadModel>? downloads = await apiService.GetAsync<IEnumerable<EpisodeDownloadModel>?>("getDownloads");

            if (downloads is null || !downloads.Any())
            {
                SetCronJobDownloads(0, 0);
                SetCronJobState(CronJobState.WaitForNextCycle);

                CronJobErrorEvent?.Invoke(MessageType.Info, InfoMessage.NoDownloadsInQueue);
                return;
            }

            SetCronJobState(CronJobState.Running);

            DownloadQue = downloads.EnqueueRange();
            ConverterService.CTS = new CancellationTokenSource();

            while (DownloadQue!.Count != 0)
            {
                if (ConverterService.CTS is not null && ConverterService.CTS.IsCancellationRequested && !ConverterService.AbortIsSkip)
                    break;

                EpisodeDownloadModel episodeDownload = DownloadQue.Dequeue();

                if (SkippedDownloads.Contains(episodeDownload))
                    continue;

                SetCronJobDownloads(DownloadQue.Count, 0);

                if (string.IsNullOrEmpty(episodeDownload.Download.Name))
                    continue;

                string originalEpisodeName = episodeDownload.Download.Name;

                string url = "";

                if (episodeDownload.StreamingPortal.Name == Hoster.STO.ToString())
                {
                    url = $"https://s.to/serie/stream{episodeDownload.Download.Path}/{string.Format(Globals.LinkBlueprint, episodeDownload.Download.Season, episodeDownload.Download.Episode)}";
                }
                else if (episodeDownload.StreamingPortal.Name == Hoster.AniWorld.ToString())
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
                catch (HttpRequestException ex)
                {
                    CronJobErrorEvent?.Invoke(MessageType.Error, ex.Message);
                    logger.LogError($"{DateTime.Now} | {ex.Message}");

                    hasError = true;
                    continue;
                }
                catch (Exception ex)
                {
                    CronJobErrorEvent?.Invoke(MessageType.Error, ex.Message);
                    logger.LogError($"{DateTime.Now} | {ex.Message}");

                    hasError = true;
                    continue;
                }
                finally
                {
                    if (hasError)
                    {
                        CronJobErrorEvent?.Invoke(MessageType.Warning, WarningMessage.DownloadNotRemoved);
                    }
                }

                Dictionary<Language, List<string>>? languageRedirectLinks = HosterHelper.GetLanguageRedirectLinks(html);

                if (languageRedirectLinks == null || languageRedirectLinks.Count == 0)
                    continue;

                episodeDownload.Download.Name = episodeDownload.Download.Name.GetValidFileName();

                IEnumerable<Language> episodeLanguages = episodeDownload.Download.LanguageFlag.GetFlags<Language>(ignore: Language.None);
                IEnumerable<Language> redirectLanguages = languageRedirectLinks.Keys.Where(lang => episodeLanguages.Contains(lang));

                IEnumerable<Language>? downloadLanguages = episodeLanguages.Intersect(redirectLanguages);

                int finishedDownloadsCount = 1;

                foreach (Language language in downloadLanguages)
                {
                    SetCronJobDownloads(DownloadQue.Count, downloadLanguages.Count() - finishedDownloadsCount);

                    if (episodeDownload.StreamingPortal.Name == Hoster.STO.ToString())
                    {
                        url = $"https://s.to{languageRedirectLinks[language][0]}";
                    }
                    else if (episodeDownload.StreamingPortal.Name == Hoster.AniWorld.ToString())
                    {
                        url = $"https://aniworld.to{languageRedirectLinks[language][0]}";
                    }
                    else { continue; }

                    string? m3u8Url = await GetEpisodeM3U8(url, downloaderPreferences);

                    if (string.IsNullOrEmpty(m3u8Url))
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

                    CommandResultExt? result = await converterService.StartDownload(m3u8Url, episodeDownload.Download, settings.DownloadPath, downloaderPreferences);

                    finishedDownloadsCount++;

                    if (result is not null && result.Skipped)
                    {
                        CronJobErrorEvent?.Invoke(MessageType.Secondary, InfoMessage.EpisodeDownloadSkipped);

                        continue;
                    }

                    if (result is not null && result.SkippedNoResult)
                    {
                        CronJobErrorEvent?.Invoke(MessageType.Secondary, InfoMessage.EpisodeDownloadSkippedFileExists);

                        await RemoveDownload(episodeDownload);

                        continue;
                    }

                    if (ConverterService.CTS is not null && ( result is null || !result.IsSuccess ))
                    {
                        if (ConverterService.CTS.IsCancellationRequested)
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

                    if (result is not null && result.IsSuccess)
                    {
                        CronJobErrorEvent?.Invoke(MessageType.Success, InfoMessage.DownloadFinished);

                        if (finishedDownloadsCount >= downloadLanguages.Count())
                            await RemoveDownload(episodeDownload);
                    }
                }

                if (StopMarkDownload is not null && StopMarkDownload.Download == episodeDownload.Download)
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
            if (CronJobEvent is not null)
            {
                foreach (Delegate d in CronJobEvent.GetInvocationList())
                {
                    CronJobEvent -= (CronJobEventHandler)d;
                }
            }

            if (CronJobErrorEvent is not null)
            {
                foreach (Delegate d in CronJobErrorEvent.GetInvocationList())
                {
                    CronJobErrorEvent -= (CronJobErrorEventHandler)d;
                }
            }

            if (CronJobDownloadsEvent is not null)
            {
                foreach (Delegate d in CronJobDownloadsEvent.GetInvocationList())
                {
                    CronJobDownloadsEvent -= (CronJobDownloadsEventHandler)d;
                }
            }
        }

        public async Task Abort()
        {
            if (Browser is not null)
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
                Args = ["--no-sandbox", ( downloaderPreferences.UseProxy ? $"--proxy-server={downloaderPreferences.ProxyUri}" : "" )]
            });


            using IPage? page = await Browser.NewPageAsync();

            if (downloaderPreferences.UseProxy)
            {
                await page.AuthenticateAsync(new Credentials
                {
                    Username = downloaderPreferences.ProxyUsername,
                    Password = downloaderPreferences.ProxyPassword
                });
            }

            try
            {
                string? videoPageHtml = await GetPageHtml(page, streamUrl);

                if (string.IsNullOrEmpty(videoPageHtml))
                    return default;

                return await GetVideoSource(page, videoPageHtml);
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

        private async Task<string?> GetVideoSource(IPage page, string html)
        {
            HtmlDocument htmlDocument = new();
            htmlDocument.LoadHtml(html);

            Match hlsMatch = new Regex("'hls': '((?'DN'https://delivery-node)?.*?)',").Match(html);

            if (hlsMatch.Success)
            {
                if (hlsMatch.Groups["DN"].Success)
                    return HttpUtility.HtmlDecode(hlsMatch.Groups[1].Value);

                return await ExtractHLSViaJS(page, hlsMatch.Groups[1].Value);
            }

            Match m3u8NodeMatch = new Regex("https://delivery-node-(.*?)\\);").Match(html);

            if (m3u8NodeMatch.Success)
                return HttpUtility.HtmlDecode(m3u8NodeMatch.Value.TrimEnd('"', ')', ';'));

            Match sourceMatch = new Regex("<source src=\"(.*?)\" type=\"application/x-mpegurl\" data-vds=\"\">").Match(html);

            if (sourceMatch.Success)
                return HttpUtility.HtmlDecode(sourceMatch.Groups[1].Value);

            Match playerSourceMatch = new Regex("<source src=\"(.*?)\"").Match(html);

            if (playerSourceMatch.Success)
                return HttpUtility.HtmlDecode(playerSourceMatch.Groups[1].Value);

            return null;
        }

        private async Task<string?> ExtractHLSViaJS(IPage page, string source)
        {
            string atob = $"atob(\"{source}\")";

            System.Text.Json.JsonElement? jsonElement = await page.EvaluateExpressionAsync(atob);

            if (jsonElement != null && jsonElement.HasValue)
                return jsonElement.Value.GetString();

            return null;
        }

        private async Task<string?> GetPageHtml(IPage page, string pageUrl)
        {
            await page.GoToAsync(pageUrl);

            return await page.GetContentAsync();
        }

        private async Task<(bool, string?)> TryWaitForSelectorAsync(IPage page, string selector, int timeout = 5000, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await Task.Run(async () =>
            {
                try
                {
                    await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Timeout = timeout });
                    logger.LogInformation($"{DateTime.Now} | Working query selector: {selector}");
                    return (true, selector);
                }
                catch (WaitTaskTimeoutException)
                {
                    return (false, default!);
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"{DateTime.Now} | {ex}");
                    return (false, default!);
                }
            }, cancellationToken);
        }

        private async Task RemoveDownload(EpisodeDownloadModel episodeDownload)
        {
            bool removeSuccess = await apiService.RemoveFinishedDownload(episodeDownload);

            if (!removeSuccess)
            {
                CronJobErrorEvent?.Invoke(MessageType.Warning, WarningMessage.DownloadNotRemoved);
            }
        }
    }
}
