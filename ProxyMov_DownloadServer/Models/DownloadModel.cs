namespace ProxyMov_DownloadServer.Models;

public class DownloadModel
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public Language LanguageFlag { get; set; }
    public TimeSpan StreamDuration { get; set; }
    public DateTime DownloadStartTime { get; set; }
}