namespace ProxyMov_DownloadServer.Models
{
    public class EpisodeDownloadModel
    {
        public bool HasStopMark { get; set; }
        public DownloadModel Download { get; set; } = default!;
        public StreamingPortalModel StreamingPortal { get; set; } = default!;
    }
}
