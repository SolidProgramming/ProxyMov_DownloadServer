namespace ProxyMov_DownloadServer.Interfaces;

internal interface IApiService
{
    bool Init();
    Task<bool> RemoveFinishedDownload(EpisodeDownloadModel download);
    Task<bool> SendCaptchaNotification(HosterModel hoster);
    Task<bool> SetDownloaderPreferences(DownloaderPreferencesModel downloaderPreferences);
    Task<T?> GetAsync<T>(string uri);
    Task<T?> GetAsync<T>(string uri, Dictionary<string, string> queryData, object body);
    Task<T?> GetAsync<T>(string uri, Dictionary<string, string> queryData);
    Task<T?> PostAsync<T>(string uri, object value);
    Task<bool> PostAsync(string uri, object value);
}