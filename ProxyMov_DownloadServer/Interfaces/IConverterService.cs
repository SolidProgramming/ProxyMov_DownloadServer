namespace ProxyMov_DownloadServer.Interfaces
{
    internal interface IConverterService
    {
        bool Init();
        Task<CommandResultExt?> StartDownload(string streamUrl, DownloadModel download, string downloadPath, DownloaderPreferencesModel downloaderPreferences);
    }
}
