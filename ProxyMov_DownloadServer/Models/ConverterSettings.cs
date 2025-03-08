using Newtonsoft.Json;

namespace ProxyMov_DownloadServer.Models
{
    public class ConverterSettingsModel
    {
        [JsonProperty("FileFormat")]
        public FileFormat FileFormat { get; set; }

        [JsonProperty("VideoCodec")]
        public VideoCodec VideoCodec { get; set; }

        [JsonProperty("AudioCodec")]
        public AudioCodec AudioCodec { get; set; }
    }
}
