using Newtonsoft.Json;

namespace ProxyMov_DownloadServer.Models;

public class ConverterSettingsModel
{
    [JsonProperty("FileFormat")] public FileFormat FileFormat { get; set; } = FileFormat.ORIGINAL;

    [JsonProperty("VideoCodec")] public VideoCodec VideoCodec { get; set; } = VideoCodec.ORIGINAL;

    [JsonProperty("AudioCodec")] public AudioCodec AudioCodec { get; set; } = AudioCodec.ORIGINAL;
}