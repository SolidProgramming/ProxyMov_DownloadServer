using Microsoft.Extensions.Http.Resilience;
using Polly;
using System.Net;

namespace ProxyMov_DownloadServer.Factories
{
    public class HttpClientFactory : Interfaces.IHttpClientFactory
    {
        private readonly System.Net.Http.IHttpClientFactory httpClientFactory;
        private readonly ILoggerFactory loggerFactory;

        public HttpClientFactory(System.Net.Http.IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            this.httpClientFactory = httpClientFactory;
            this.loggerFactory = loggerFactory;
        }

        public HttpClient CreateHttpClient<T>(bool defaultRequestHeaders = true)
        {
            HttpClient httpClient = httpClientFactory.CreateClient(typeof(T).FullName ?? typeof(T).Name);
            ApplyDefaultConfiguration(httpClient, defaultRequestHeaders);
            return httpClient;
        }

        public HttpClient CreateHttpClient<T>(WebProxy proxy, bool defaultRequestHeaders = true)
        {
            CookieContainer cookieContainer = new();
            string clientName = typeof(T).FullName ?? typeof(T).Name;

            HttpClientHandler clientHandler = new()
            {
                UseProxy = true,
                Proxy = proxy,
                MaxConnectionsPerServer = 1,
                UseCookies = true,
                CookieContainer = cookieContainer
            };

            ResiliencePipelineBuilder<HttpResponseMessage> pipelineBuilder = new();
            HttpResiliencePipelineConfigurator.Configure(pipelineBuilder, loggerFactory, clientName);

            ResilienceHandler resilienceHandler = new(pipelineBuilder.Build())
            {
                InnerHandler = clientHandler
            };

            HttpClient httpClient = new(resilienceHandler)
            {
                Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite)
            };

            ApplyDefaultConfiguration(httpClient, defaultRequestHeaders);

            return httpClient;
        }

        private static void ApplyDefaultConfiguration(HttpClient httpClient, bool defaultRequestHeaders)
        {
            if (!defaultRequestHeaders)
                return;

            if (!httpClient.DefaultRequestHeaders.Contains("X-Requested-With"))
            {
                httpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            }

            if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 101.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36 OPR/91.0.4516.72");
            }
        }
    }
}
