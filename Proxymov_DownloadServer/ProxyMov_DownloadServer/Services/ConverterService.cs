using CliWrap;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProxyMov_DownloadServer.Services;

public class ConverterService(ILogger<ConverterService> logger, IHostApplicationLifetime appLifetime)
    : IConverterService
{
    public delegate void ConverterStateChangedEvent(ConverterState state, DownloadModel? download = null);

    public delegate void ConvertProgressChangedEvent(ConvertProgressModel convertProgress);

    public delegate void ConvertStartedEvent(DownloadModel download);

    private const int MaxGetStreamDurationRetries = 3;
    private const int MaxGetVideoFileInfoRetries = 2;

    private static long PreviousDownloadSize;
    private static DateTime? FFMPEGStartTime;
    private static DownloadModel? Download { get; set; }
    private static ConverterState ConverterState { get; set; } = ConverterState.Undefined;

    private bool IsInitialized { get; set; }
    public static bool AbortIsSkip { get; set; }

    public static CancellationTokenSource? CTS { get; set; }

    public bool Init()
    {
        appLifetime.ApplicationStopping.Register(() => { Abort(); });

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

    public async Task<CommandResultExt?> StartDownload(EpisodeDownloadModel episode, string downloadPath, DownloaderPreferencesModel downloaderPreferences, ConverterSettingsModel? converterSettings, Language language)
    {
        if (!IsInitialized)
        {
            logger.LogError($"{DateTime.Now} | {ErrorMessage.ConverterServiceNotInitialized}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(episode.M3U8Url))
        {
            throw new ArgumentException("M3U8 URL is null or empty.", nameof(episode.M3U8Url));
        }

        VideoFileInfoModel? videoFileInfo = await GetVideoFileInfo(episode.M3U8Url, downloaderPreferences, converterSettings);

        if (videoFileInfo == null)
        {
            logger.LogError($"{DateTime.Now} | No stream duration found!");
            return null;
        }

        episode.VideoFileInfo = videoFileInfo;

        string filePath = GetFileName(episode, downloadPath, converterSettings, language);

        TimeSpan streamDuration = await GetStreamDuration(episode.M3U8Url, downloaderPreferences);

        if (streamDuration == TimeSpan.Zero)
        {
            logger.LogError($"{DateTime.Now} | No stream duration found!");
            return null;
        }

        episode.Download.StreamDuration = streamDuration;

        if (File.Exists(filePath))
        {
            TimeSpan durationExistingFile = TimeSpan.Zero;
            durationExistingFile = await GetStreamDurationFromFile(filePath);

            if (durationExistingFile.Minutes == streamDuration.Minutes &&
                durationExistingFile.Seconds == streamDuration.Seconds)
            {
                logger.LogInformation($"{DateTime.Now} | {InfoMessage.EpisodeDownloadSkippedFileExists}");

                return new CommandResultExt(skippedNoResult: true);
            }
        }

        Download = episode.Download;

        ConverterStateChanged?.Invoke(ConverterState.Downloading, episode.Download);

        string args = "";
        string proxyAuthArgs = "";

        if (downloaderPreferences.UseProxy && !string.IsNullOrWhiteSpace(downloaderPreferences.ProxyUri))
        {
            string proxyUri = downloaderPreferences.ProxyUri.Replace("http://", "").Replace("https://", "");
            proxyAuthArgs =
                $"-http_proxy http://{downloaderPreferences.ProxyUsername}:{downloaderPreferences.ProxyPassword}@{proxyUri}";
        }

        string hwAccell = "-hwaccel cuda -hwaccel_output_format cuda";
        bool useHwAccell = false;

        if (converterSettings is not null)
        {
            if (converterSettings.VideoCodec == VideoCodec.H264NVENC ||
                converterSettings.VideoCodec == VideoCodec.H265NVENC)
            {
                useHwAccell = true;
            }

            string mapArgs = "";

            if (episode.VideoFileInfo.VideoStreamIndex.HasValue)
                mapArgs += $"-map 0:{episode.VideoFileInfo.VideoStreamIndex.Value} ";

            if (episode.VideoFileInfo.AudioStreamIndex.HasValue)
                mapArgs += $"-map 0:{episode.VideoFileInfo.AudioStreamIndex.Value} ";

            if (string.IsNullOrWhiteSpace(mapArgs))
                mapArgs = "-map 0:v:0 -map 0:a:0 ";

            args =
                $"-y {(downloaderPreferences.UseProxy ? proxyAuthArgs : "")} {(useHwAccell ? hwAccell : "")} -i \"{episode.M3U8Url}\" {mapArgs}-acodec {converterSettings.AudioCodec.ToAudioCodec()} -vcodec {converterSettings.VideoCodec.ToVideoCodec()} -sn \"{filePath}\" -f {converterSettings.FileFormat.ToFileFormat()}";
        }
        else
        {
            logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettings}");
        }

        string binPath = Helper.GetFFMPEGPath();

        ConvertStarted?.Invoke(episode.Download);

        Download.DownloadStartTime = DateTime.Now;

        CTS = new CancellationTokenSource();

        CommandResultExt? result = null;

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
            if (File.Exists(filePath)) File.Delete(filePath);

            if (AbortIsSkip)
                return new CommandResultExt(skipped: true);
            else
                logger.LogWarning($"{DateTime.Now} | {WarningMessage.DownloadCanceled}");
        }
        catch (Exception ex)
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            logger.LogError($"{DateTime.Now} |FFMPEG: {ex}");
        }
        finally
        {
            Download = null;
            FFMPEGStartTime = null;
            PreviousDownloadSize = 0;
            ConverterStateChanged?.Invoke(ConverterState.Idle);
        }

        return result;
    }

    public static event ConverterStateChangedEvent? ConverterStateChanged;
    public static event ConvertProgressChangedEvent? ConvertProgressChanged;
    public static event ConvertStartedEvent? ConvertStarted;

    private void ConverterService_ConverterStateChanged(ConverterState state, DownloadModel? download = null)
    {
        ConverterState = state;

        if (state == ConverterState.Downloading && download is not null)
            logger.LogInformation(
                $"{DateTime.Now} | Downloading: {download.Name}_S{download.Season:D2}E{download.Episode:D2}");
        else
            logger.LogInformation($"{DateTime.Now} | {InfoMessage.ConverterChangedState} {state}");
    }

    private static string GetFileName(EpisodeDownloadModel episode, string downloadPath,
        ConverterSettingsModel? converterSettings, Language language)
    {
        const string releaseGroup = "ProxyMov";
        string folderPath = "";
        string seriesFolderPath = "";
        string seasonFolderName;
        string episodeFolderName;

        string? languageName = language.ToLanguageName();

        string targetVideoCodec = converterSettings is not null
            ? (converterSettings.VideoCodec == VideoCodec.ORIGINAL
                ? (episode.VideoFileInfo.VideoCodec?.ToUpperInvariant() ?? "UNKNOWN")
                : converterSettings.VideoCodec.ToVideoCodecName().ToUpperInvariant())
            : (episode.VideoFileInfo.VideoCodec?.ToUpperInvariant() ?? "UNKNOWN");

        string targetAudioCodec = converterSettings is not null
            ? (converterSettings.AudioCodec == AudioCodec.ORIGINAL
                ? (episode.VideoFileInfo.AudioCodec?.ToUpperInvariant() ?? "UNKNOWN")
                : converterSettings.AudioCodec.ToAudioCodecName().ToUpperInvariant())
            : (episode.VideoFileInfo.AudioCodec?.ToUpperInvariant() ?? "UNKNOWN");

        string fileInfo = $"[{episode.VideoFileInfo.Resolution}][{targetAudioCodec}][{targetVideoCodec}]";

        seasonFolderName = $"Season {episode.Download.Season:D2}";
        episodeFolderName = $"{episode.Download.Name} - S{episode.Download.Season:D2}E{episode.Download.Episode:D2} ({languageName}) {fileInfo}-{releaseGroup}".GetValidFileName();

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            folderPath = @"/data/downloads";

            seriesFolderPath = Path.Combine(folderPath, episode.Download.Name);

            folderPath = Path.Combine(seriesFolderPath, seasonFolderName);
        }
        else
        {
            seriesFolderPath = Path.Combine(downloadPath, episode.Download.Name);

            folderPath = Path.Combine(downloadPath, episode.Download.Name, seasonFolderName);
        }

        if (!Directory.Exists(seriesFolderPath)) Directory.CreateDirectory(seriesFolderPath);

        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        string filePath = $"{Path.Combine(folderPath, episodeFolderName)}.ts";

        if (converterSettings is not null && converterSettings.FileFormat != FileFormat.ORIGINAL)
        {
            return Path.ChangeExtension(filePath, converterSettings.FileFormat.ToFileFormat());
        }

        return Path.Combine(folderPath, episodeFolderName).GetValidFileName();
    }

    private static void ReadOutput(string output)
    {
        if (!FFMPEGStartTime.HasValue) FFMPEGStartTime = DateTime.Now;

        if (!Regex.IsMatch(output, "time=([\\d:]+)")) return;

        ConvertProgressModel progress = new();

        try
        {
            Match bitrateMatch = Regex.Match(output, "bitrate=\\s*(\\d+\\.?\\d*)kbits/s");

            if (!bitrateMatch.Success) return;

            string bitrateText = bitrateMatch.Groups[1].Value;
            progress.Bitrate = double.Parse(bitrateText);

            Match sizeMatch = Regex.Match(output, "size=\\s*(\\d+)Ki?B time", RegexOptions.IgnoreCase);

            if (!sizeMatch.Success) return;

            string sizeText = sizeMatch.Groups[1].Value;
            progress.Size = long.Parse(sizeText);

            Match timeMatch = Regex.Match(output, "time=([\\d:]+)");

            if (!timeMatch.Success) return;

            string timeText = timeMatch.Groups[1].Value;
            progress.Time = TimeSpan.Parse(timeText);

            long bytesDownloaded = progress.Size * 1024 - PreviousDownloadSize;

            if (bytesDownloaded > 0)
            {
                progress.KBytePerSecond = bytesDownloaded / 1024 / (DateTime.Now - FFMPEGStartTime).Value.TotalSeconds;
                PreviousDownloadSize = progress.Size;
            }

            double progressPercent = 100.0d * (progress.Time.TotalSeconds / Download!.StreamDuration.TotalSeconds);

            if (progressPercent <= 0.0) return;

            progress.ProgressPercent = Convert.ToInt32(progressPercent);

            Match speedMatch = Regex.Match(output, "speed=.*?(\\d+(?:[\\.\\,]\\d+)*)");

            if (!speedMatch.Success) return;

            string speedText = speedMatch.Groups[1].Value;
            progress.EncodingSpeed = float.Parse(speedText);

            Match fpsMatch = Regex.Match(output, "fps=.*?(\\d+(?:[\\.\\,]\\d+)*)");

            if (!fpsMatch.Success) return;

            string fpsText = fpsMatch.Groups[1].Value;
            progress.FPS = float.Parse(fpsText);

            progress.ETA = EstimateCompletionTime(progress.ProgressPercent, Download.DownloadStartTime);

            ConvertProgressChanged?.Invoke(progress);
        }
        catch (Exception)
        {
        }
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
            proxyAuthArgs =
                $"-http_proxy http://{downloaderPreferences.ProxyUsername}:{downloaderPreferences.ProxyPassword}@{proxyUri}";
        }

        ffProbeArgs =
            $"{(downloaderPreferences.UseProxy ? proxyAuthArgs : "")} -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 -sexagesimal \"{streamUrl}\"";

        TimeSpan streamDuration;

        for (int attempt = 1; attempt <= MaxGetStreamDurationRetries; attempt++)
        {
            streamDuration = await TryGetStreamDuration(ffProbeArgs, attempt);

            if (streamDuration != TimeSpan.Zero)
            {
                return streamDuration;
            }
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
            logger.LogWarning(
                $"{DateTime.Now} | Failed to get stream duration! Retries left: {MaxGetStreamDurationRetries - attemptCount}");
            return TimeSpan.Zero;
        }
        catch (Exception ex)
        {
            logger.LogError($"{DateTime.Now} |FFPROBE: {ex}");
            return TimeSpan.Zero;
        }

        string stdOut = stdOutBuffer.ToString();

        if (string.IsNullOrEmpty(stdOut)) return TimeSpan.Zero;

        if (TimeSpan.TryParse(stdOut, out TimeSpan duration)) return duration;

        return TimeSpan.Zero;
    }

    private static async Task<TimeSpan> GetStreamDurationFromFile(string filePath)
    {
        StringBuilder StdOutBuffer = new();

        string binPath = Helper.GetFFProbePath();

        string arguments =
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 -sexagesimal \"{filePath}\"";

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

        if (string.IsNullOrEmpty(output) || output == "N/A") return TimeSpan.Zero;

        return TimeSpan.Parse(output);
    }

    public async Task<VideoFileInfoModel?> GetVideoFileInfo(string streamUrl, DownloaderPreferencesModel downloaderPreferences, ConverterSettingsModel? converterSettings)
    {
        VideoFileInfoModel? videoQualityInfo;

        for (int attempt = 1; attempt <= MaxGetVideoFileInfoRetries; attempt++)
        {
            videoQualityInfo = await TryGetVideoFileInfo(streamUrl, downloaderPreferences, converterSettings, attempt);

            if (videoQualityInfo is not null)
                return videoQualityInfo;
        }

        return null;
    }

    private async Task<VideoFileInfoModel?> TryGetVideoFileInfo(string streamUrl, DownloaderPreferencesModel downloaderPreferences, ConverterSettingsModel? converterSettings, int attemptCount)
    {
        int waitDuration = attemptCount * 10;

        StringBuilder stdOutBuffer = new();

        string ffProbeBinPath = Helper.GetFFProbePath();

        string proxyAuthArgs = "";
        if (downloaderPreferences.UseProxy && !string.IsNullOrWhiteSpace(downloaderPreferences.ProxyUri))
        {
            string proxyUri = downloaderPreferences.ProxyUri.Replace("http://", "").Replace("https://", "");
            proxyAuthArgs = $"-http_proxy http://{downloaderPreferences.ProxyUsername}:{downloaderPreferences.ProxyPassword}@{proxyUri}";
        }

        string arguments = $"{(downloaderPreferences.UseProxy ? proxyAuthArgs : "")} -v error -print_format json -show_streams \"{streamUrl}\"";

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(waitDuration));

        try
        {
            await Cli.Wrap(ffProbeBinPath)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
                .ExecuteAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning($"{DateTime.Now} | Failed to get stream/video quality info! Retries left: {MaxGetVideoFileInfoRetries - attemptCount}");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError($"{DateTime.Now} | FFPROBE Info: {ex.Message}");
            return null;
        }

        string json = stdOutBuffer.ToString();
        stdOutBuffer.Clear();

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            FFProbeMetadataInfo? metadata = JsonConvert.DeserializeObject<FFProbeMetadataInfo>(json);
            if (metadata?.Streams is null || metadata.Streams.Count == 0)
                return null;

            string? preferredVideoCodec = NormalizePreferredVideoCodec(converterSettings?.VideoCodec ?? VideoCodec.ORIGINAL);
            string? preferredAudioCodec = NormalizePreferredAudioCodec(converterSettings?.AudioCodec ?? AudioCodec.ORIGINAL);
            VideoResolution preferredResolution = converterSettings?.PreferredVideoResolution ?? VideoResolution.BEST;

            Models.Stream? bestVideo =
                metadata.Streams
                    .Where(s => s.CodecType == "video")
                    .OrderByDescending(s => GetResolutionPriority(s.Height, preferredResolution))
                    .ThenByDescending(s => ParseBitrate(s.Tags?.VariantBitrate) ?? 0)
                    .ThenByDescending(s => IsPreferredCodec(s.CodecName, preferredVideoCodec))
                    .ThenByDescending(s => GetVideoCodecScore(s.CodecName))
                    .FirstOrDefault()
            ??
                metadata.Streams
                    .Where(s => s.CodecType == "video")
                    .OrderByDescending(s => s.Height ?? 0)
                    .ThenByDescending(s => ParseBitrate(s.Tags?.VariantBitrate) ?? 0)
                    .ThenByDescending(s => GetVideoCodecScore(s.CodecName))
                    .FirstOrDefault();

            Models.Stream? bestAudio =
                metadata.Streams
                    .Where(s => s.CodecType == "audio" &&
                                preferredAudioCodec is not null &&
                                string.Equals(s.CodecName, preferredAudioCodec, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => ParseBitrate(s.Tags?.VariantBitrate) ?? 0)
                    .FirstOrDefault()
            ??
                metadata.Streams
                    .Where(s => s.CodecType == "audio")
                    .OrderByDescending(s => ParseBitrate(s.Tags?.VariantBitrate) ?? 0)
                    .ThenByDescending(s => GetAudioCodecScore(s.CodecName))
                    .FirstOrDefault();

            return new VideoFileInfoModel
            {
                Resolution = bestVideo?.Height is int h ? $"{h}p" : null,
                VideoCodec = bestVideo?.CodecName,
                AudioCodec = bestAudio?.CodecName,
                VideoStreamIndex = bestVideo?.Index,
                AudioStreamIndex = bestAudio?.Index
            };
        }
        catch (Exception ex)
        {
            logger.LogError($"{DateTime.Now} | Failed to parse video quality info: {ex.Message}");
            return null;
        }
    }

    private static long? ParseBitrate(string? bitrateText)
    {
        if (long.TryParse(bitrateText, out long value) && value > 0)
            return value;

        return null;
    }

    private static int GetVideoCodecScore(string? codecName) => codecName?.ToLowerInvariant() switch
    {
        "hevc" or "h265" => 400,
        "h264" => 300,
        "vp9" => 200,
        "vp8" => 100,
        _ => 0
    };

    private static int GetAudioCodecScore(string? codecName) => codecName?.ToLowerInvariant() switch
    {
        "ac3" => 300,
        "aac" => 200,
        "mp3" => 100,
        _ => 0
    };

    private static string? NormalizePreferredVideoCodec(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264",
        VideoCodec.H264NVENC => "h264",
        VideoCodec.H265 => "hevc",
        VideoCodec.H265NVENC => "hevc",
        VideoCodec.VP8 => "vp8",
        VideoCodec.VP9 => "vp9",
        VideoCodec.MPEG4 => "mpeg4",
        _ => null
    };

    private static string? NormalizePreferredAudioCodec(AudioCodec codec) => codec switch
    {
        AudioCodec.AAC => "aac",
        AudioCodec.AC3 => "ac3",
        AudioCodec.MP3 => "mp3",
        _ => null
    };

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

        if (CTS is not null && !CTS.Token.IsCancellationRequested) CTS.Cancel();
    }

    private static int GetResolutionPriority(int? height, VideoResolution preference)
    {
        int h = height ?? 0;

        if (preference == VideoResolution.BEST)
            return h;

        int target = (int)preference;

        if (h == target)
            return 1_000_000 + h;

        int distance = Math.Abs(h - target);
        return 500_000 - distance;
    }

    private static int IsPreferredCodec(string? streamCodec, string? preferredCodec)
    {
        if (string.IsNullOrWhiteSpace(preferredCodec))
            return 0;

        return string.Equals(streamCodec, preferredCodec, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }
}