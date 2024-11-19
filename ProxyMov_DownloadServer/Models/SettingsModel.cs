using Newtonsoft.Json;

namespace ProxyMov_DownloadServer.Models
{
    internal class SettingsModel
    {
        [JsonProperty("ApiUrl")]
        public string ApiUrl { get; set; } = default!;

        [JsonProperty("APIKey")]
        public string? ApiKey { get; set; }

        [JsonProperty("DownloadPath")]
        public string? DownloadPath { get; set; } = default!;

    }
}
