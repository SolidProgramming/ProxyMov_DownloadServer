using System.ComponentModel.DataAnnotations;

namespace ProxyMov_DownloadServer.Models;

public class DownloaderPreferencesModel
{
    [Required(ErrorMessageResourceType = typeof(int), ErrorMessage = "Bitte eine Zahl eingeben")]
    public int Interval { get; set; }

    public bool AutoStart { get; set; }
    public bool TelegramCaptchaNotification { get; set; }
    public bool UseProxy { get; set; }
    public string? ProxyUri { get; set; }
    public string? ProxyUsername { get; set; }
    public string? ProxyPassword { get; set; }
}