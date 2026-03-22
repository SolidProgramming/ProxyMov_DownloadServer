namespace ProxyMov_DownloadServer.Models;

public class EpisodeDownloadModel
{
    public bool HasStopMark { get; set; }
    public DownloadModel Download { get; set; } = null!;
    public StreamingPortalModel StreamingPortal { get; set; } = null!;
    public string? M3U8Url { get; set; }
    public VideoFileInfo VideoFileInfo { get; set; } = null!;
}