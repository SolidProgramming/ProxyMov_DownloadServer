namespace ProxyMov_DownloadServer.Models;

public class HosterModel
{
    public HosterModel(string host, StreamingPortal hoster, string browserUrl)
    {
        Host = host;
        Hoster = hoster;
        BrowserUrl = browserUrl;
    }

    public string Host { get; set; }
    public StreamingPortal Hoster { get; set; }
    public string BrowserUrl { get; set; }
}