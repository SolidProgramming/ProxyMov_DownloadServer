namespace ProxyMov_DownloadServer.Models;

public class ActiveDownloadModel(EpisodeDownloadModel episode, Language language, CancellationTokenSource cts)
{
    public Guid Id { get; } = Guid.NewGuid();
    public EpisodeDownloadModel Episode { get; } = episode;
    public Language Language { get; } = language;
    public ConvertProgressModel? Progress { get; set; }
    public TimeSpan SmoothedETA { get; set; }
    public bool IsSkipRequested { get; set; }

    internal CancellationTokenSource CTS { get; } = cts;
    internal long PreviousDownloadSize { get; set; }
    internal DateTime? FFMPEGStartTime { get; set; }
}
