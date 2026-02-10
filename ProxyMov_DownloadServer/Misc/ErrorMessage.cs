namespace ProxyMov_DownloadServer.Misc;

internal class ErrorMessage
{
    internal const string ReadSettings = "Couldn't read settings!";
    internal const string ReadSettingsApiUrl = "Couldn't read/parse API Url!";
    internal const string ReadSettingsApiKey = "Couldn't read/parse API Key!";
    internal const string WrongCredentials = "Login failed! Check credentials!";
    internal const string FFMPEGBinarieNotFound = "Couldn't find FFMPEG binaries!";
    internal const string FFProbeBinariesNotFound = "Couldn't find FFProbe binaries!";
    internal const string DBInitFailed = "DB init failed!";

    internal const string HosterUnavailable =
        "Hoster blocked or unavailable!\nPlease open your Browser and navigate to the hoster and resolve the captcha.";

    internal const string ProcessNotAssociated = "Process not associated. No need to kill.";

    internal const string APIServiceNotInitialized =
        "API Service is not initialized! Call IApiService.Init() on startup.";

    internal const string ConverterServiceNotInitialized =
        "Converter Service is not initialized! Call IConverterService.Init() on startup.";

    internal const string CronJobNotInitialized = "Cron Job/Service is not initialized! Call c on startup.";
    internal const string HttpRequestException = "Error requesting data from hoster!";

    internal const string SkipDownloadFail =
        "Beim Überspringen des Downloads für die ausgewählte Episode ist ein Fehler aufgetreten!";

    internal const string RemoveDownloadFail =
        "Beim Löschen des Downloads für die ausgewählte Episode ist ein Fehler aufgetreten!";

    internal const string RemoveDBEpisodeDownloadFail =
        "Beim löschen des Downloads auf Datenbankebene ist ein Fehler aufgetreten!";

    internal const string DownloaderPreferencesSaveFail =
        "Beim Speichern der Einstellungen ist ein Fehler aufgetreten!";

    internal const string RetrieveDownloaderPreferencesFail =
        "Beim Abrufen der Einstellungen ist ein Fehler aufgetreten!";
}