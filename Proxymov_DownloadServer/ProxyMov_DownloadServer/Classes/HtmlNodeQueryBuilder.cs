using System.Text;
using HtmlAgilityPack;

namespace ProxyMov_DownloadServer.Classes;

internal class HtmlNodeQueryBuilder
{
    /// <summary>
    ///     Return all resulting nodes from the list
    /// </summary>
    internal List<HtmlNode> Results { get; private set; } = [];

    /// <summary>
    ///     Returns only the first node result from the list
    /// </summary>
    internal HtmlNode? Result => Results.Count > 0 ? Results[0] : null;

    internal HtmlNodeQueryBuilder Query(HtmlDocument doc)
    {
        Results.Add(doc.DocumentNode);

        return this;
    }

    internal HtmlNodeQueryBuilder Query(HtmlNode node)
    {
        Results.Add(node);

        return this;
    }

    internal HtmlNodeQueryBuilder Query(List<HtmlNode> nodes)
    {
        Results.AddRange(nodes);

        return this;
    }

    internal HtmlNodeQueryBuilder ById(string id)
    {
        string query = $".//*[@id='{id}']";

        Results = GetNodesByQuery(query);

        return this;
    }

    internal HtmlNodeQueryBuilder ByElement(string elementName)
    {
        string query = ".//" + elementName;

        Results = GetNodesByQuery(query);

        return this;
    }

    internal HtmlNodeQueryBuilder ByClass(params string[] classNames)
    {
        StringBuilder _builder = new();

        foreach (string className in classNames)
        {
            _builder.Append(className);
            _builder.Append(' ');
        }

        _builder.Remove(_builder.Length - 1, 1);

        string query = $".//*[@class='{_builder}']";

        Results = GetNodesByQuery(query);

        return this;
    }

    internal HtmlNodeQueryBuilder ByAttribute(params string[] attributeNames)
    {
        StringBuilder _builder = new();

        foreach (string className in attributeNames)
        {
            _builder.Append(className);
            _builder.Append(' ');
        }

        _builder.Remove(_builder.Length - 1, 1);

        string query = $".//*[@{_builder}]";

        Results = GetNodesByQuery(query);

        return this;
    }

    internal HtmlNodeQueryBuilder ByAttributeValue(string attributeName, string attributeValue)
    {
        string query = $".//*[@{attributeName}='{attributeValue}']";

        Results = GetNodesByQuery(query);

        return this;
    }

    internal HtmlNodeQueryBuilder ByAttributeValues(string attributeName, List<string> attributeValues)
    {
        List<HtmlNode> filteredNodes = [];
        foreach (string attValue in attributeValues)
        {
            string query = $".//*[@{attributeName}='{attValue}']";

            filteredNodes.AddRange(GetNodesByQuery(query));
        }

        Results = filteredNodes;

        return this;
    }

    internal List<HtmlNode> GetNodesByQuery(string query)
    {
        List<HtmlNode> nodes = [];

        for (int i = 0; i < Results?.Count; i++)
        {
            List<HtmlNode>? newNodes = Results[i].SelectNodes(query)?.ToList();

            if (newNodes is null) continue;

            nodes.AddRange(newNodes);
        }

        return nodes;
    }
}