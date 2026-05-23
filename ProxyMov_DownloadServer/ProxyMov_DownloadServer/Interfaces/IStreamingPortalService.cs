using System.Net;

namespace ProxyMov_DownloadServer.Interfaces
{
    public interface IStreamingPortalService
    {
        string BaseUrl { get; init; }
        string Name { get; init; }
        StreamingPortal StreamingPortal { get; init; }
        Task<bool> InitAsync(WebProxy? proxy = null);
        HttpClient? GetHttpClient();
        Task<List<DirectViewLinkModel>?> GetDirectViewLinksAsync(EpisodeDownloadModel episode);
        string GetHosterEpisodeUrl(EpisodeDownloadModel episode);
    }
}
