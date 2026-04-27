using System.Net;

namespace ProxyMov_DownloadServer.Classes
{
    public abstract class StreamingPortalServiceBase<TService> : IStreamingPortalService
    {
        private HttpClient? httpClient;

        protected StreamingPortalServiceBase(
            ILogger logger,
            Interfaces.IHttpClientFactory httpClientFactory,
            string baseUrl,
            string name,
            StreamingPortal streamingPortal)
        {
            Logger = logger;
            HttpClientFactory = httpClientFactory;
            BaseUrl = baseUrl;
            Name = name;
            StreamingPortal = streamingPortal;
        }

        protected ILogger Logger { get; }
        protected Interfaces.IHttpClientFactory HttpClientFactory { get; }
        protected HttpClient HttpClient => httpClient ?? throw new InvalidOperationException($"{Name} service is not initialized.");

        public string BaseUrl { get; init; }
        public string Name { get; init; }
        public StreamingPortal StreamingPortal { get; init; }

        public async Task<bool> InitAsync(WebProxy? proxy = null)
        {
            httpClient = proxy is null
                ? HttpClientFactory.CreateHttpClient<TService>()
                : HttpClientFactory.CreateHttpClient<TService>(proxy);

            (bool success, string? ipv4) = await HttpClient.GetIPv4();

            if (!success)
            {
                Logger.LogError($"{DateTime.UtcNow.ToLocalTime()} | {Name} Service unable to retrieve WAN IP");
            }
            else
            {
                Logger.LogInformation($"{DateTime.UtcNow.ToLocalTime()} | {Name} Service initialized with WAN IP {ipv4}");
            }

            return success;
        }

        public HttpClient? GetHttpClient()
        {
            return httpClient;
        }

        protected async Task<List<DirectViewLinkModel>?> GetDirectViewLinksAsync(string url)
        {
            string html = await HttpClient.GetStringAsync(url);
            return GetLanguageRedirectLinks(html);
        }

        protected abstract List<DirectViewLinkModel>? GetLanguageRedirectLinks(string html);
        public abstract Task<List<DirectViewLinkModel>?> GetDirectViewLinksAsync(EpisodeDownloadModel episode);
    }
}
