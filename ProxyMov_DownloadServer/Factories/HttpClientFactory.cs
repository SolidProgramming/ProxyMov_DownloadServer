using System.Net;

namespace ProxyMov_DownloadServer.Factories
{
    public class HttpClientFactory
    {
        private static Dictionary<Type, HttpClient> HttpClients = [];

        public static HttpClient CreateHttpClient(WebProxy proxy, bool defaultRequestHeaders = true)
        {
            return _CreateHttpClient(defaultRequestHeaders, proxy);
        }

        public static HttpClient CreateHttpClient(bool defaultRequestHeaders = true)
        {
            return _CreateHttpClient(defaultRequestHeaders);
        }

        public static HttpClient CreateHttpClient<T>(bool defaultRequestHeaders = true)
        {
            if (HttpClients.ContainsKey(typeof(T)))
                return HttpClients[typeof(T)];

            return _CreateHttpClient<T>(defaultRequestHeaders);
        }

        public static HttpClient CreateHttpClient<T>(WebProxy proxy, bool defaultRequestHeaders = true)
        {
            if (HttpClients.ContainsKey(typeof(T)))
                return HttpClients[typeof(T)];

            return _CreateHttpClient<T>(defaultRequestHeaders, proxy);
        }

        #region private methods
        private static HttpClient _CreateHttpClient<T>(bool defaultRequestHeaders, WebProxy? proxy = null)
        {
            CookieContainer cookieContainer = new();

            HttpClientHandler clientHandler = new()
            {
                UseProxy = proxy is not null,
                Proxy = proxy,
                MaxConnectionsPerServer = 1,
                UseCookies = true,
                CookieContainer = cookieContainer
            };

            HttpClient httpClient = new(clientHandler)
            {
                Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite)
            };

            if (defaultRequestHeaders)
            {
                httpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 101.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36 OPR/91.0.4516.72");
            }

            HttpClients.Add(typeof(T), httpClient);

            return httpClient;
        }
        private static HttpClient _CreateHttpClient(bool defaultRequestHeaders, WebProxy? proxy = null)
        {
            CookieContainer cookieContainer = new();

            HttpClientHandler clientHandler = new()
            {
                UseProxy = proxy is not null,
                Proxy = proxy,
                MaxConnectionsPerServer = 1,
                UseCookies = true,
                CookieContainer = cookieContainer
            };

            HttpClient httpClient = new(clientHandler)
            {
                Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite)
            };

            if (defaultRequestHeaders)
            {
                httpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 101.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36 OPR/91.0.4516.72");
            }

            return httpClient;
        }
        #endregion
    }
}
