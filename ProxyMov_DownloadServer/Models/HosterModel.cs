namespace ProxyMov_DownloadServer.Models
{
    public class HosterModel
    {
        public string Host { get; set; }
        public Hoster Hoster { get; set; }
        public string BrowserUrl { get; set; }
        public HosterModel(string host, Hoster hoster, string browserUrl)
        {
            Host = host;
            Hoster = hoster;
            BrowserUrl = browserUrl;
        }
    }
}
