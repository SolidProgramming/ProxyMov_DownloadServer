namespace ProxyMov_DownloadServer.Models
{
    public class VideoFileInfoModel
    {
        public string? Resolution { get; set; }
        public VideoResolution? VideoResolution { get; set; } = null;
        public string? VCodec { get; set; }
        public VideoCodec? VideoCodec { get; set; } = null;
        public string? ACodec { get; set; }
        public AudioCodec? AudioCodec { get; set; } = null;
        public int? VideoStreamIndex { get; set; }
        public int? AudioStreamIndex { get; set; }
    }
}
