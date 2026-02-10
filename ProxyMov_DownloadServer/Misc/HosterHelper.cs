using System.Net;
using HtmlAgilityPack;

namespace ProxyMov_DownloadServer.Misc;

internal static class HosterHelper
{
    private static readonly List<HosterModel> SupportedHoster = new()
    {
        new HosterModel("s.to", Hoster.STO, "https://s.to/"),
        new HosterModel("aniworld.to", Hoster.AniWorld, "https://aniworld.to/")
    };

    internal static async Task<bool> HosterReachable(HosterModel hoster, WebProxy? proxy = null)
    {
        HttpClient? httpClient = null;

        try
        {
            if (proxy is null)
                httpClient = HttpClientFactory.CreateHttpClient();
            else
                httpClient = HttpClientFactory.CreateHttpClient(proxy);

            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var responseMessage = await httpClient.GetAsync(hoster.BrowserUrl);

            if (!responseMessage.IsSuccessStatusCode) return false;

            var html = await responseMessage.Content.ReadAsStringAsync();

            httpClient.Dispose();

            if (string.IsNullOrEmpty(html)) return false;

            return !CaptchaRequired(html);
        }
        catch (Exception)
        {
            httpClient?.Dispose();
            return false;
        }
    }

    //ToDo: Hoster Name dynamisch in den query einbinden
    internal static Dictionary<Language, List<string>>? GetLanguageRedirectLinks(string html)
    {
        Dictionary<Language, List<string>> languageRedirectLinks = [];

        HtmlDocument document = new();
        document.LoadHtml(html);

        List<HtmlNode>? languageRedirectNodes = new HtmlNodeQueryBuilder()
            .Query(document)
            .GetNodesByQuery("//div/a/i[contains(@title, 'Hoster')]");

        if (languageRedirectNodes == null || languageRedirectNodes.Count == 0) return null;

        List<string> redirectLinks;


        redirectLinks = GetLanguageRedirectLinksNodes(Language.GerDub);

        if (redirectLinks.Count > 0) languageRedirectLinks.Add(Language.GerDub, redirectLinks);

        redirectLinks = GetLanguageRedirectLinksNodes(Language.EngDub);

        if (redirectLinks.Count > 0) languageRedirectLinks.Add(Language.EngDub, redirectLinks);

        redirectLinks = GetLanguageRedirectLinksNodes(Language.EngSub);

        if (redirectLinks.Count > 0) languageRedirectLinks.Add(Language.EngSub, redirectLinks);

        redirectLinks = GetLanguageRedirectLinksNodes(Language.GerSub);

        if (redirectLinks.Count > 0) languageRedirectLinks.Add(Language.GerSub, redirectLinks);

        redirectLinks = GetLanguageRedirectLinksNodes(Language.EngDubGerSub);

        if (redirectLinks.Count > 0) languageRedirectLinks.Add(Language.EngDubGerSub, redirectLinks);

        return languageRedirectLinks;


        List<string> GetLanguageRedirectLinksNodes(Language language)
        {
            List<HtmlNode> redirectNodes = languageRedirectNodes.Where(_ =>
                    _.ParentNode.ParentNode.ParentNode.Attributes["data-lang-key"].Value == language.ToVOELanguageKey())
                .ToList();
            List<string> filteredRedirectLinks = [];

            foreach (var node in redirectNodes)
            {
                if (node == null ||
                    node.ParentNode == null ||
                    node.ParentNode.ParentNode == null ||
                    node.ParentNode.ParentNode.ParentNode == null ||
                    !node.ParentNode.ParentNode.ParentNode.Attributes.Contains("data-link-target"))
                    continue;

                filteredRedirectLinks.Add(node.ParentNode.ParentNode.ParentNode.Attributes["data-link-target"].Value);
            }

            return filteredRedirectLinks;
        }
    }

    private static bool CaptchaRequired(string html)
    {
        return html.Contains("Browser Check");
    }

    internal static HosterModel? GetHosterByEnum(Hoster hoster)
    {
        if (SupportedHoster.Any(h => h.Hoster == hoster)) return SupportedHoster.First(h => h.Hoster == hoster);

        return null;
    }
}