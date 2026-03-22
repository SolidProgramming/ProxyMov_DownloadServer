namespace ProxyMov_DownloadServer.Interfaces;

internal interface IConverterService
{
    bool Init();

    Task<CommandResultExt?> StartDownload(EpisodeDownloadModel episode, string downloadPath,
        DownloaderPreferencesModel downloaderPreferences, ConverterSettingsModel? converterSettings, Language language);
}