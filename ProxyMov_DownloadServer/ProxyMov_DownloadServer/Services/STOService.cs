using HtmlAgilityPack;

namespace ProxyMov_DownloadServer.Services
{
    public class STOService(ILogger<STOService> logger, Interfaces.IHttpClientFactory httpClientFactory)
        : StreamingPortalServiceBase<STOService>(logger, httpClientFactory, "https://s.to", "S.TO", StreamingPortal.STO)
    {
        public override Task<List<DirectViewLinkModel>?> GetDirectViewLinksAsync(EpisodeDownloadModel episode)
        {
            string url =
                $"{BaseUrl}/serie{episode.Download.Path}/{string.Format(Globals.LinkBlueprint, episode.Download.Season, episode.Download.Episode)}";

            return GetDirectViewLinksAsync(url);
        }
        public override string GetHosterEpisodeUrl(EpisodeDownloadModel episode)
        {
            return $"{BaseUrl}/serie{episode.Download.Path}/{string.Format(Globals.LinkBlueprint, episode.Download.Season, episode.Download.Episode)}";
        }

        protected override List<DirectViewLinkModel>? GetLanguageRedirectLinks(string html)
        {
            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode>? redirectNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery("//div[@id='episode-links']//button[@data-play-url]");

            if (redirectNodes is null || redirectNodes.Count == 0)
                return null;

            List<DirectViewLinkModel> directViewLinks = [];

            foreach (HtmlNode node in redirectNodes)
            {
                string redirectUrl = node.GetAttributeValue("data-play-url", string.Empty);
                string languageLabel = node.GetAttributeValue("data-language-label", string.Empty);

                if (string.IsNullOrEmpty(redirectUrl) || string.IsNullOrEmpty(languageLabel))
                    continue;

                Language? language = languageLabel switch
                {
                    "Deutsch" => Language.GerDub,
                    "Englisch" => Language.EngDub,
                    "Ger-Sub" => Language.GerSub,
                    _ => null
                };

                if (language is null || directViewLinks.Any(_ => _.Language == language))
                    continue;

                directViewLinks.Add(new DirectViewLinkModel
                {
                    Language = language.Value,
                    DirectLink = new Uri(new Uri(BaseUrl), redirectUrl).ToString()
                });
            }

            return directViewLinks.Count > 0 ? directViewLinks : null;
        }
    }
}
