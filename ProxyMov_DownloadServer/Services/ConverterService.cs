using CliWrap;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ProxyMov_DownloadServer.Services
{
    public class ConverterService(ILogger<ConverterService> logger, IHostApplicationLifetime appLifetime)
        : IConverterService
    {
        public delegate void ConverterStateChangedEvent(ConverterState state, DownloadModel? download = default);
        public static event ConverterStateChangedEvent? ConverterStateChanged;

        public delegate void ConvertProgressChangedEvent(ConvertProgressModel convertProgress);
        public static event ConvertProgressChangedEvent? ConvertProgressChanged;

        public delegate void ConvertStartedEvent(DownloadModel download);
        public static event ConvertStartedEvent? ConvertStarted;
        private static DownloadModel? Download { get; set; }
        private static ConverterState ConverterState { get; set; } = ConverterState.Undefined;

        private bool IsInitialized { get; set; }
        public static bool AbortIsSkip { get; set; }

        public static CancellationTokenSource? CTS { get; set; }

        private static long PreviousDownloadSize = 0;
        private static DateTime? FFMPEGStartTime;

        private const int MaxGetStreamDurationRetries = 3;

        public bool Init()
        {
            appLifetime.ApplicationStopping.Register(() =>
            {
                Abort();
            });

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (!File.Exists(Helper.GetFFMPEGPath()))
                {
                    logger.LogError($"{DateTime.Now} | {ErrorMessage.FFMPEGBinarieNotFound}");
                    return false;
                }

                if (!File.Exists(Helper.GetFFProbePath()))
                {
                    logger.LogError($"{DateTime.Now} | {ErrorMessage.FFProbeBinariesNotFound}");
                    return false;
                }
            }
            ConverterStateChanged += ConverterService_ConverterStateChanged;

            IsInitialized = true;

            logger.LogInformation($"{DateTime.Now} | {InfoMessage.ConverterServiceInit}");

            return true;
        }

        private void ConverterService_ConverterStateChanged(ConverterState state, DownloadModel? download = null)
        {
            ConverterState = state;
            logger.LogInformation($"{DateTime.Now} | {InfoMessage.ConverterChangedState} {state}");
        }

        public async Task<CommandResultExt?> StartDownload(string streamUrl, DownloadModel download, string downloadPath, DownloaderPreferencesModel downloaderPreferences)
        {
            if (!IsInitialized)
            {
                logger.LogError($"{DateTime.Now} | {ErrorMessage.ConverterServiceNotInitialized}");
                return default;
            }

            string filePath = GetFileName(download, downloadPath);

            TimeSpan streamDuration = await GetStreamDuration(streamUrl, downloaderPreferences);

            if (streamDuration == TimeSpan.Zero)
            {
                logger.LogError($"{DateTime.Now} | No stream duration found!");
                return default;
            }

            download.StreamDuration = streamDuration;

            if (File.Exists(filePath))
            {
                TimeSpan durationExistingFile = TimeSpan.Zero;
                durationExistingFile = await GetStreamDurationFromFile(filePath);

                if (durationExistingFile.Minutes == streamDuration.Minutes && durationExistingFile.Seconds == streamDuration.Seconds)
                {
                    logger.LogInformation($"{DateTime.Now} | {InfoMessage.EpisodeDownloadSkippedFileExists}");

                    return new CommandResultExt(skippedNoResult: true);
                }
            }

            Download = download;

            ConverterStateChanged?.Invoke(ConverterState.Downloading, download);

            string args = "";
            string proxyAuthArgs = "";

            if (downloaderPreferences.UseProxy && !string.IsNullOrWhiteSpace(downloaderPreferences.ProxyUri))
            {
                string proxyUri = downloaderPreferences.ProxyUri.Replace("http://", "").Replace("https://", "");
                proxyAuthArgs = $"-http_proxy http://{downloaderPreferences.ProxyUsername}:{downloaderPreferences.ProxyPassword}@{proxyUri}";
            }

            string proxyLogText = $"| Url: {downloaderPreferences.ProxyUri}@{downloaderPreferences.ProxyUsername}";
            logger.LogInformation($"{DateTime.Now} | Use Download Proxy: {downloaderPreferences.UseProxy} {( downloaderPreferences.UseProxy ? proxyLogText : "" )}");

            args = $"-y {( downloaderPreferences.UseProxy ? proxyAuthArgs : "" )} -i \"{streamUrl}\" -acodec copy -vcodec copy -sn \"{filePath}\" -f matroska";

            string binPath = Helper.GetFFMPEGPath();

            ConvertStarted?.Invoke(download);

            Download.DownloadStartTime = DateTime.Now;

            CTS = new CancellationTokenSource();

            CommandResultExt? result = default;

            try
            {
                CommandResult tempResult = await Cli.Wrap(binPath)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.ZeroExitCode)
                    .WithStandardErrorPipe(PipeTarget.ToDelegate(ReadOutput, Encoding.UTF8))
                    .ExecuteAsync(CTS.Token);

                result = new CommandResultExt(tempResult.ExitCode, tempResult.StartTime, tempResult.ExitTime);
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                if (AbortIsSkip)
                {
                    return new CommandResultExt(skipped: true);
                }
                else
                {
                    logger.LogWarning($"{DateTime.Now} | {WarningMessage.DownloadCanceled}");
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                logger.LogError($"{DateTime.Now} |FFMPEG: {ex}");
            }
            finally
            {
                Download = default;
                FFMPEGStartTime = default;
                PreviousDownloadSize = 0;
                ConverterStateChanged?.Invoke(ConverterState.Idle);
            }

            return result;
        }

        private static string GetFileName(DownloadModel download, string downloadPath)
        {
            string folderPath = "";
            string seriesFolderPath = "";
            string seasonFolderName;
            string episodeFolderName;

            seasonFolderName = $"S{download.Season:D2}";
            episodeFolderName = $"E{download.Episode:D2}";

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                folderPath = @"/data/downloads";

                seriesFolderPath = Path.Combine(folderPath, download.Name);

                folderPath = Path.Combine(seriesFolderPath, seasonFolderName);
            }
            else
            {
                seriesFolderPath = Path.Combine(downloadPath, download.Name);

                folderPath = Path.Combine(downloadPath, download.Name, seasonFolderName);
            }

            if (!Directory.Exists(seriesFolderPath))
                Directory.CreateDirectory(seriesFolderPath);

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            return Path.Combine(folderPath, $"{download.Name}_{seasonFolderName}{episodeFolderName}.mkv");
        }

        private static void ReadOutput(string output)
        {
            if (!FFMPEGStartTime.HasValue)
            {
                FFMPEGStartTime = DateTime.Now;
            }

            if (!Regex.IsMatch(output, "time=([\\d:]+)"))
                return;

            ConvertProgressModel progress = new();

            try
            {
                Match bitrateMatch = Regex.Match(output, "bitrate=\\s*(\\d+\\.?\\d*)kbits/s");

                if (!bitrateMatch.Success)
                    return;

                string bitrateText = bitrateMatch.Groups[1].Value;
                progress.Bitrate = double.Parse(bitrateText);

                Match sizeMatch = Regex.Match(output, "size=\\s*(\\d+)Ki?B time", RegexOptions.IgnoreCase);

                if (!sizeMatch.Success)
                    return;

                string sizeText = sizeMatch.Groups[1].Value;
                progress.Size = long.Parse(sizeText);

                Match timeMatch = Regex.Match(output, "time=([\\d:]+)");

                if (!timeMatch.Success)
                    return;

                string timeText = timeMatch.Groups[1].Value;
                progress.Time = TimeSpan.Parse(timeText);

                long bytesDownloaded = progress.Size * 1024 - PreviousDownloadSize;

                if (bytesDownloaded > 0)
                {
                    progress.KBytePerSecond = bytesDownloaded / 1024 / ( DateTime.Now - FFMPEGStartTime ).Value.TotalSeconds;
                    PreviousDownloadSize = progress.Size;
                }

                double progressPercent = 100.0d * ( progress.Time.TotalSeconds / Download!.StreamDuration.TotalSeconds );

                if (progressPercent <= 0.0)
                    return;

                progress.ProgressPercent = Convert.ToInt32(progressPercent);

                Match speedMatch = Regex.Match(output, "speed=.*?(\\d+(?:[\\.\\,]\\d+)*)");

                if (!speedMatch.Success)
                    return;

                string speedText = speedMatch.Groups[1].Value;
                progress.EncodingSpeed = float.Parse(speedText);

                Match fpsMatch = Regex.Match(output, "fps=.*?(\\d+(?:[\\.\\,]\\d+)*)");

                if (!fpsMatch.Success)
                    return;

                string fpsText = fpsMatch.Groups[1].Value;
                progress.FPS = float.Parse(fpsText);

                progress.ETA = EstimateCompletionTime(progress.ProgressPercent, Download.DownloadStartTime);

                ConvertProgressChanged?.Invoke(progress);
            }
            catch (Exception) { }
        }

        private static TimeSpan EstimateCompletionTime(double completedPercentage, DateTime downloadStartTime)
        {
            TimeSpan elapsedTime = DateTime.Now.Subtract(downloadStartTime);

            double remainingPercentage = 100 - completedPercentage;
            double estimatedTotalTime = elapsedTime.TotalSeconds / completedPercentage * 100;
            double remainingTime = estimatedTotalTime * remainingPercentage / 100;
            TimeSpan remainingTimeSpan = TimeSpan.FromSeconds(remainingTime);

            return remainingTimeSpan;
        }

        private async Task<TimeSpan> GetStreamDuration(string streamUrl, DownloaderPreferencesModel downloaderPreferences)
        {
            string ffProbeArgs = "";
            string proxyAuthArgs = "";

            if (downloaderPreferences.UseProxy && !string.IsNullOrWhiteSpace(downloaderPreferences.ProxyUri))
            {
                string proxyUri = downloaderPreferences.ProxyUri.Replace("http://", "").Replace("https://", "");
                proxyAuthArgs = $"-http_proxy http://{downloaderPreferences.ProxyUsername}:{downloaderPreferences.ProxyPassword}@{proxyUri}";
            }

            ffProbeArgs = $"{( downloaderPreferences.UseProxy ? proxyAuthArgs : "" )} -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 -sexagesimal \"{streamUrl}\"";

            TimeSpan streamDuration;

            for (int attempt = 1; attempt < MaxGetStreamDurationRetries; attempt++)
            {
                streamDuration = await TryGetStreamDuration(ffProbeArgs, attempt);

                if (streamDuration != TimeSpan.Zero)
                    return streamDuration;
            }

            return TimeSpan.Zero;
        }

        private async Task<TimeSpan> TryGetStreamDuration(string ffProbeArgs, int attemptCount)
        {
            int waitDuration = attemptCount * 5;

            StringBuilder stdOutBuffer = new();

            CancellationTokenSource cts = new(TimeSpan.FromSeconds(waitDuration));

            string ffProbeBinPath = Helper.GetFFProbePath();

            try
            {
                await Cli.Wrap(ffProbeBinPath)
                .WithArguments(ffProbeArgs)
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
                        .ExecuteAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning($"{DateTime.Now} | Failed to get stream duration! Retries left: {MaxGetStreamDurationRetries - attemptCount}");
                return TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                logger.LogError($"{DateTime.Now} |FFPROBE: {ex}");
                return TimeSpan.Zero;
            }

            string? stdOut = stdOutBuffer.ToString();

            if (string.IsNullOrEmpty(stdOut))
                return TimeSpan.Zero;

            logger.LogInformation($"{DateTime.Now} | Found stream duration on attempt: {attemptCount}");
            return TimeSpan.Parse(stdOut);
        }

        private static async Task<TimeSpan> GetStreamDurationFromFile(string filePath)
        {
            StringBuilder StdOutBuffer = new();

            string? binPath = Helper.GetFFProbePath();

            string arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 -sexagesimal \"{filePath}\"";

            CancellationTokenSource ffProbeCTS = new(TimeSpan.FromSeconds(5));
            CancellationToken ffProbeToken = ffProbeCTS.Token;

            try
            {
                await Cli.Wrap(binPath!)
                         .WithArguments(arguments)
                         .WithValidation(CommandResultValidation.None)
                             .WithStandardOutputPipe(PipeTarget.ToStringBuilder(StdOutBuffer))
                                 //.WithStandardErrorPipe(PipeTarget.ToStringBuilder(StdErrBuffer))
                                 .ExecuteAsync(ffProbeToken);
            }
            catch (OperationCanceledException)
            {
                return TimeSpan.Zero;
            }
            finally
            {
                ffProbeCTS.Dispose();
            }

            string output = StdOutBuffer.ToString().TrimEnd();

            StdOutBuffer.Clear();

            if (string.IsNullOrEmpty(output) || output == "N/A")
                return TimeSpan.Zero;

            return TimeSpan.Parse(output);
        }

        public static DownloadModel? GetDownload()
        {
            return Download;
        }

        public static ConverterState GetConverterState()
        {
            return ConverterState;
        }

        public static void Abort(bool isSkip = false)
        {
            AbortIsSkip = isSkip;

            if (CTS is not null && !CTS.Token.IsCancellationRequested)
            {
                CTS.Cancel();
            }
        }
    }
}
