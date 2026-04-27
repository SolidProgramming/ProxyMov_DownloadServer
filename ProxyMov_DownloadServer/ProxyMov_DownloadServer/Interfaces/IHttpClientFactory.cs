using System.Net;

namespace ProxyMov_DownloadServer.Interfaces
{
    public interface IHttpClientFactory
    {
        HttpClient CreateHttpClient<T>(bool defaultRequestHeaders = true);
        HttpClient CreateHttpClient<T>(WebProxy proxy, bool defaultRequestHeaders = true);
    }
}
