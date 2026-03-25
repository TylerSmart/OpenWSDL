using System.Xml.Linq;
using OpenWSDL;

namespace OpenWSDL.Wsdl;

internal sealed class SchemaIndex
{
    private readonly Dictionary<(string Ns, string Name), XElement> _globalElements = new();
    private readonly Dictionary<(string Ns, string Name), XElement> _globalTypes = new();
    private readonly List<(string SchemaNs, XElement Schema)> _schemas = new();

    public void IndexDocuments(IEnumerable<XDocument> documents)
    {
        _globalElements.Clear();
        _globalTypes.Clear();
        _schemas.Clear();
        XNamespace xsd = WsdlNamespaces.Xsd;

        foreach (var doc in documents)
        {
            foreach (var schema in doc.Descendants(xsd + "schema"))
            {
                var tns = (string?)schema.Attribute("targetNamespace") ?? "";
                _schemas.Add((tns, schema));

                foreach (var el in schema.Elements(xsd + "element"))
                {
                    var name = (string?)el.Attribute("name");
                    if (!string.IsNullOrEmpty(name))
                        _globalElements[(tns, name)] = el;
                }

                foreach (var ct in schema.Elements(xsd + "complexType"))
                {
                    var name = (string?)ct.Attribute("name");
                    if (!string.IsNullOrEmpty(name))
                        _globalTypes[(tns, name)] = ct;
                }

                foreach (var st in schema.Elements(xsd + "simpleType"))
                {
                    var name = (string?)st.Attribute("name");
                    if (!string.IsNullOrEmpty(name))
                        _globalTypes[(tns, name)] = st;
                }
            }
        }
    }

    public XElement? FindGlobalElement(XmlQName q)
    {
        if (q.IsEmpty)
            return null;
        return _globalElements.GetValueOrDefault((q.Namespace, q.LocalName));
    }

    public XElement? FindGlobalType(XmlQName q)
    {
        if (q.IsEmpty)
            return null;
        return _globalTypes.GetValueOrDefault((q.Namespace, q.LocalName));
    }

    public bool UsesQualifiedElementForm(XmlQName elementQName)
    {
        var el = FindGlobalElement(elementQName);
        if (el is null)
            return true;
        XNamespace xsd = WsdlNamespaces.Xsd;
        var schema = el.Ancestors(xsd + "schema").FirstOrDefault();
        if (schema is null)
            return true;
        var form = (string?)schema.Attribute("elementFormDefault");
        return !string.Equals(form, "unqualified", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<(string SchemaNs, XElement Schema)> Schemas => _schemas;

    public string? GetPrefixForNamespace(XElement schema, string nsUri)
    {
        foreach (var attr in schema.Attributes())
        {
            if (attr.IsNamespaceDeclaration)
            {
                if (attr.Value == nsUri)
                    return string.IsNullOrEmpty(attr.Name.LocalName) ? "" : attr.Name.LocalName;
            }
        }

        return null;
    }
}
