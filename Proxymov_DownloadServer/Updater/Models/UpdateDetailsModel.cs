using System.Xml.Serialization;

namespace Updater.Models;

[XmlRoot("item")]
public class UpdateDetailsModel
{
    [XmlElement(ElementName = "version")] public string? Version { get; set; }

    [XmlElement(ElementName = "url")] public string? AssemblyUrl { get; set; }

    [XmlElement(ElementName = "changelog")]
    public string? Changelog { get; set; }

    [XmlElement(ElementName = "mandatory")]
    public bool Mandatory { get; set; }
}