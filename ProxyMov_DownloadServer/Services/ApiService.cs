using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;

namespace ProxyMov_DownloadServer.Services;

public class ApiService(ILogger<ApiService> logger, HttpClient httpClient)
    : IApiService
{
    private string? ApiKey;
    private bool IsInitialized;

    public bool Init()
    {
        var settings = SettingsHelper.ReadSettings<SettingsModel>();

        if (settings is null)
        {
            logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettings}");
            return false;
        }

        if (!Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out var apiUri))
        {
            logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettingsApiUrl}");
            return false;
        }

        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettingsApiKey}");
            return false;
        }

        httpClient.BaseAddress = apiUri;

        ApiKey = settings.ApiKey;

        IsInitialized = true;

        logger.LogInformation($"{DateTime.Now} | {InfoMessage.ApiServiceInit}");

        return true;
    }

    public async Task<bool> RemoveFinishedDownload(EpisodeDownloadModel download)
    {
        return await PostAsync<bool>("removeFinishedDownload", download);
    }

    public async Task<bool> SendCaptchaNotification(HosterModel hoster)
    {
        return await GetAsync<bool>("captchaNotify",
            new Dictionary<string, string> { { "streamingPortal", hoster.Host } });
    }

    public async Task<bool> SetDownloaderPreferences(DownloaderPreferencesModel downloaderPreferences)
    {
        return await PostAsync<bool>("setDownloaderPreferences", downloaderPreferences);
    }

    public async Task<T?> GetAsync<T>(string uri)
    {
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        return await SendRequest<T>(request);
    }

    public async Task<T?> GetAsync<T>(string uri, Dictionary<string, string> queryData, object body)
    {
        HttpRequestMessage request =
            new(HttpMethod.Get, new Uri(QueryHelpers.AddQueryString(httpClient.BaseAddress + uri, queryData!)))
            {
                Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
            };
        return await SendRequest<T>(request);
    }

    public async Task<T?> GetAsync<T>(string uri, Dictionary<string, string> queryData)
    {
        HttpRequestMessage request = new(HttpMethod.Get,
            new Uri(QueryHelpers.AddQueryString(httpClient.BaseAddress + uri, queryData!)));
        return await SendRequest<T>(request);
    }

    public async Task<T?> PostAsync<T>(string uri, object value)
    {
        HttpRequestMessage request = new(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json")
        };
        return await SendRequest<T>(request);
    }

    public async Task<bool> PostAsync(string uri, object value)
    {
        HttpRequestMessage request = new(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json")
        };
        return await SendRequest<bool>(request);
    }

    private async Task<T?> SendRequest<T>(HttpRequestMessage request)
    {
        if (!IsInitialized)
        {
            logger.LogError($"{DateTime.Now} | {ErrorMessage.APIServiceNotInitialized}");
            return default;
        }

        request.Headers.Add("X-API-KEY", ApiKey);

        using var response = await httpClient.SendAsync(request);

        // auto logout on 401 response
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            UserStorageHelper.Set(null!);
            return default;
        }

        // throw exception on error response
        if (!response.IsSuccessStatusCode) return default;

        if (typeof(T) == typeof(bool)) return (T)Convert.ChangeType(response.IsSuccessStatusCode, typeof(T));

        return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync());
    }
}