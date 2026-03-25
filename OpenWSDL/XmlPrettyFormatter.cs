using System.Xml;
using System.Xml.Linq;

namespace OpenWSDL;

internal static class XmlPrettyFormatter
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Indent = true,
        IndentChars = "   ",
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        OmitXmlDeclaration = true,
        ConformanceLevel = ConformanceLevel.Document,
    };

    /// <summary>Pretty-prints XML for readable bodies in Postman / OpenAPI. Returns the original string if parsing fails.</summary>
    public static string Indent(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return xml;

        try
        {
            var doc = XDocument.Parse(xml, LoadOptions.None);
            using var stringWriter = new StringWriter();
            using (var xmlWriter = XmlWriter.Create(stringWriter, WriterSettings))
            {
                doc.Save(xmlWriter);
            }

            return stringWriter.ToString().TrimEnd();
        }
        catch (XmlException)
        {
            return xml;
        }
        catch (InvalidOperationException)
        {
            return xml;
        }
    }
}
