namespace ProxyMov_DownloadServer.Models;

public class HosterModel
{
    public HosterModel(string host, Hoster hoster, string browserUrl)
    {
        Host = host;
        Hoster = hoster;
        BrowserUrl = browserUrl;
    }

    public string Host { get; set; }
    public Hoster Hoster { get; set; }
    public string BrowserUrl { get; set; }
}