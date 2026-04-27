namespace ProxyMov_DownloadServer.Factories
{
    public interface IStreamingPortalServiceFactory
    {
        void AddService(StreamingPortal streamingPortal, IServiceProvider serviceProvider);
        IStreamingPortalService GetService(StreamingPortal streamingPortal);
    }
}
