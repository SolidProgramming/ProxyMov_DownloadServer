using HtmlAgilityPack;

namespace ProxyMov_DownloadServer.Services
{
    public class AniWorldService(ILogger<AniWorldService> logger, Interfaces.IHttpClientFactory httpClientFactory)
        : StreamingPortalServiceBase<AniWorldService>(logger, httpClientFactory, "https://aniworld.to", "AniWorld", StreamingPortal.AniWorld)
    {
        public override Task<List<DirectViewLinkModel>?> GetDirectViewLinksAsync(EpisodeDownloadModel episode)
        {
            string url =
                $"{BaseUrl}/anime/stream{episode.Download.Path}/{string.Format(Globals.LinkBlueprint, episode.Download.Season, episode.Download.Episode)}";

            return GetDirectViewLinksAsync(url);
        }

        protected override List<DirectViewLinkModel>? GetLanguageRedirectLinks(string html)
        {
            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode> languageRedirectNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery("//div/a/i[contains(@title, 'Hoster')]");

            if (languageRedirectNodes.Count == 0)
                return null;

            List<DirectViewLinkModel> directViewLinks = [];

            AddRedirectLink(Language.GerDub);
            AddRedirectLink(Language.EngDub);
            AddRedirectLink(Language.EngSub);
            AddRedirectLink(Language.GerSub);
            AddRedirectLink(Language.EngDubGerSub);

            return directViewLinks.Count > 0 ? directViewLinks : null;

            void AddRedirectLink(Language language)
            {
                string? redirectLink = GetLanguageRedirectLink(language);

                if (string.IsNullOrEmpty(redirectLink))
                    return;

                directViewLinks.Add(new DirectViewLinkModel
                {
                    Language = language,
                    DirectLink = new Uri(new Uri(BaseUrl), redirectLink).ToString()
                });
            }

            string? GetLanguageRedirectLink(Language language)
            {
                List<HtmlNode> redirectNodes = languageRedirectNodes
                    .Where(_ => _.ParentNode.ParentNode.ParentNode.Attributes["data-lang-key"].Value == language.ToVOELanguageKey())
                    .ToList();

                foreach (HtmlNode node in redirectNodes)
                {
                    if (node.ParentNode?.ParentNode?.ParentNode is not HtmlNode parentNode ||
                        !parentNode.Attributes.Contains("data-link-target"))
                        continue;

                    return parentNode.Attributes["data-link-target"].Value;
                }

                return null;
            }
        }
    }
}
