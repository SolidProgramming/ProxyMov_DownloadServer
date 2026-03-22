namespace ProxyMov_DownloadServer.Models
{
    public class VideoFileInfoModel
    {
        public string? Resolution { get; set; }
        public string? VideoCodec { get; set; }
        public string? AudioCodec { get; set; }
        public int? VideoStreamIndex { get; set; }
        public int? AudioStreamIndex { get; set; }
    }
}
