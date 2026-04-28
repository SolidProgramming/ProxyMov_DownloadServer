using Newtonsoft.Json;

namespace ProxyMov_DownloadServer.Models;

internal class SettingsModel
{
    [JsonProperty(nameof(ApiUrl))]
    public string? ApiUrl { get; set; }

    [JsonProperty("APIKey")]
    public string? ApiKey { get; set; }

    [JsonProperty(nameof(DownloadPath))]
    public string? DownloadPath { get; set; }

    [JsonProperty(nameof(ConverterSettings))]
    public ConverterSettingsModel? ConverterSettings { get; set; }
}