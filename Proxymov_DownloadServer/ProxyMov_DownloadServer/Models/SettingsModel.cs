using Newtonsoft.Json;

namespace ProxyMov_DownloadServer.Models;

internal class SettingsModel
{
    public const int MaxConcurrentDownloadsLimit = 5;

    [JsonProperty(nameof(ApiUrl))]
    public string? ApiUrl { get; set; }

    [JsonProperty("APIKey")]
    public string? ApiKey { get; set; }

    [JsonProperty(nameof(DownloadPath))]
    public string? DownloadPath { get; set; }

    [JsonProperty(nameof(ConverterSettings))]
    public ConverterSettingsModel? ConverterSettings { get; set; }

    [JsonProperty(nameof(MaxConcurrentDownloads))]
    public int MaxConcurrentDownloads { get; set; } = 1;
}