namespace ProxyMov_DownloadServer.Factories
{
    public class StreamingPortalServiceFactory : IStreamingPortalServiceFactory
    {
        private readonly Dictionary<StreamingPortal, IStreamingPortalService> streamingPortalServices = [];

        public void AddService(StreamingPortal streamingPortal, IServiceProvider serviceProvider)
        {
            if (!streamingPortalServices.ContainsKey(streamingPortal))
            {
                streamingPortalServices.Add(streamingPortal, CreateService(streamingPortal, serviceProvider));
            }
        }

        public IStreamingPortalService GetService(StreamingPortal streamingPortal)
        {
            return streamingPortalServices[streamingPortal];
        }

        private static IStreamingPortalService CreateService(
            StreamingPortal streamingPortal,
            IServiceProvider serviceProvider)
        {
            Interfaces.IHttpClientFactory httpClientFactory =
                serviceProvider.GetRequiredService<Interfaces.IHttpClientFactory>();

            return streamingPortal switch
            {
                StreamingPortal.STO => new STOService(
                    serviceProvider.GetRequiredService<ILogger<STOService>>(),
                    httpClientFactory),
                StreamingPortal.AniWorld => new AniWorldService(
                    serviceProvider.GetRequiredService<ILogger<AniWorldService>>(),
                    httpClientFactory),
                _ => throw new NotImplementedException()
            };
        }
    }
}
