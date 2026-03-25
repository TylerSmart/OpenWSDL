using System.Text;
using System.Xml.Linq;
using OpenWSDL;

namespace OpenWSDL.Wsdl;

internal sealed class SampleXmlGenerator
{
    private readonly SchemaIndex _index;
    private readonly HashSet<string> _recursionGuard = new();

    public SampleXmlGenerator(SchemaIndex index) => _index = index;

    public string GenerateElementTree(XmlQName rootElement, string preferredPrefix, bool qualifiedElements)
    {
        _recursionGuard.Clear();
        var sb = new StringBuilder();
        var el = _index.FindGlobalElement(rootElement);
        if (el is null)
        {
            sb.Append('<').Append(EscapeQName(rootElement, preferredPrefix)).Append("/>");
            return sb.ToString();
        }

        WriteElement(sb, el, rootElement.Namespace, preferredPrefix, qualifiedElements, null);
        return sb.ToString();
    }

    private void WriteElement(StringBuilder sb, XElement elementDecl, string elementFormNs, string preferredPrefix,
        bool qualifiedElements, XmlQName? parentTypeName)
    {
        XNamespace xsd = WsdlNamespaces.Xsd;
        var schema = elementDecl.Ancestors(xsd + "schema").FirstOrDefault();
        var schemaNs = (string?)schema?.Attribute("targetNamespace") ?? "";
        var resolve = BuildPrefixResolver(elementDecl);

        var refName = (string?)elementDecl.Attribute("ref");
        if (!string.IsNullOrEmpty(refName))
        {
            var q = XmlQName.Parse(refName, resolve);
            var resolved = _index.FindGlobalElement(q);
            if (resolved is not null)
                WriteElement(sb, resolved, q.Namespace, preferredPrefix, qualifiedElements, null);
            return;
        }

        var name = (string?)elementDecl.Attribute("name") ?? "item";
        var typeAttr = (string?)elementDecl.Attribute("type");
        var nillable = elementDecl.Attribute("nillable")?.Value == "true";
        var minOccurs = ParseOccurs(elementDecl.Attribute("minOccurs")?.Value, 1);
        var maxOccurs = elementDecl.Attribute("maxOccurs")?.Value;
        var unbounded = maxOccurs == "unbounded";
        var max = unbounded ? 2 : ParseOccurs(maxOccurs, 1);

        var elementNs = qualifiedElements ? schemaNs : elementFormNs;
        var tagName = qualifiedElements ? FormatQName(name, elementNs, preferredPrefix) : name;

        var defaultVal = (string?)elementDecl.Attribute("default");
        var fixedVal = (string?)elementDecl.Attribute("fixed");
        var inlineCt = elementDecl.Element(xsd + "complexType");
        var inlineSt = elementDecl.Element(xsd + "simpleType");

        if (minOccurs == 0 && !unbounded && max == 0)
            return;

        var repeat = unbounded ? 2 : Math.Max(1, max);
        for (var i = 0; i < repeat; i++)
        {
            var guardKey = $"{tagName}:{parentTypeName}";
            if (!_recursionGuard.Add(guardKey))
            {
                sb.Append('<').Append(tagName).Append(">...</").Append(tagName).Append('>');
                continue;
            }

            try
            {
                sb.Append('<').Append(tagName);

                if (inlineCt is not null)
                {
                    WriteAttributesFromComplexType(sb, inlineCt, resolve, preferredPrefix, qualifiedElements);
                    if (ComplexTypeYieldsEmptyXmlBody(inlineCt))
                        sb.Append("/>");
                    else
                    {
                        sb.Append('>');
                        WriteParticleChildren(sb, inlineCt, schemaNs, resolve, preferredPrefix, qualifiedElements);
                        sb.Append("</").Append(tagName).Append('>');
                    }
                }
                else if (inlineSt is not null)
                {
                    sb.Append('>');
                    sb.Append(EscapeXml(fixedVal ?? defaultVal ?? SampleForSimpleType(inlineSt)));
                    sb.Append("</").Append(tagName).Append('>');
                }
                else if (!string.IsNullOrEmpty(typeAttr))
                {
                    var tq = XmlQName.Parse(typeAttr, resolve);
                    if (IsBuiltInXsd(tq))
                    {
                        sb.Append('>');
                        sb.Append(EscapeXml(fixedVal ?? defaultVal ?? SampleForBuiltIn(tq.LocalName)));
                        sb.Append("</").Append(tagName).Append('>');
                    }
                    else
                    {
                        var typeEl = _index.FindGlobalType(tq);
                        if (typeEl is null)
                        {
                            sb.Append('>');
                            sb.Append("?");
                            sb.Append("</").Append(tagName).Append('>');
                        }
                        else if (typeEl.Name.LocalName == "complexType")
                        {
                            WriteAttributesFromComplexType(sb, typeEl, resolve, preferredPrefix, qualifiedElements);
                            if (ComplexTypeYieldsEmptyXmlBody(typeEl))
                                sb.Append("/>");
                            else
                            {
                                sb.Append('>');
                                WriteParticleChildren(sb, typeEl, tq.Namespace, resolve, preferredPrefix,
                                    qualifiedElements);
                                sb.Append("</").Append(tagName).Append('>');
                            }
                        }
                        else
                        {
                            sb.Append('>');
                            sb.Append(EscapeXml(fixedVal ?? defaultVal ?? SampleForSimpleType(typeEl)));
                            sb.Append("</").Append(tagName).Append('>');
                        }
                    }
                }
                else
                {
                    var nested = elementDecl.Element(xsd + "complexType");
                    if (nested is not null)
                    {
                        WriteAttributesFromComplexType(sb, nested, resolve, preferredPrefix, qualifiedElements);
                        if (ComplexTypeYieldsEmptyXmlBody(nested))
                            sb.Append("/>");
                        else
                        {
                            sb.Append('>');
                            WriteParticleChildren(sb, nested, schemaNs, resolve, preferredPrefix, qualifiedElements);
                            sb.Append("</").Append(tagName).Append('>');
                        }
                    }
                    else
                    {
                        sb.Append('>');
                        if (nillable)
                            sb.Append("<!-- nillable -->");
                        sb.Append("</").Append(tagName).Append('>');
                    }
                }
            }
            finally
            {
                _recursionGuard.Remove(guardKey);
            }
        }
    }

    private void WriteParticleChildren(StringBuilder sb, XElement complexType, string owningTypeNs,
        Func<string, string> resolve, string preferredPrefix, bool qualifiedElements)
    {
        XNamespace xsd = WsdlNamespaces.Xsd;
        var particle = complexType.Elements().FirstOrDefault(e =>
            e.Name == xsd + "sequence" || e.Name == xsd + "choice" || e.Name == xsd + "all" || e.Name == xsd + "group");

        if (particle is null)
            return;

        foreach (var child in particle.Elements())
        {
            if (child.Name == xsd + "element")
                WriteElement(sb, child, owningTypeNs, preferredPrefix, qualifiedElements, null);
            else if (child.Name == xsd + "choice")
            {
                var firstEl = child.Elements(xsd + "element").FirstOrDefault();
                if (firstEl is not null)
                    WriteElement(sb, firstEl, owningTypeNs, preferredPrefix, qualifiedElements, null);
            }
            else if (child.Name == xsd + "any")
            {
                sb.Append("<!-- any -->");
                sb.Append('<').Append("any").Append('>').Append('?').Append("</any>");
            }
        }
    }

    private static void WriteAttributesFromComplexType(StringBuilder sb, XElement complexType,
        Func<string, string> resolve, string preferredPrefix, bool qualifiedElements)
    {
        foreach (var at in EnumerateDeclaredAttributes(complexType))
        {
            var name = (string?)at.Attribute("name");
            if (string.IsNullOrEmpty(name))
                continue;
            var use = (string?)at.Attribute("use");
            if (use == "prohibited")
                continue;
            var typeAttr = (string?)at.Attribute("type");
            var sample = "?";
            if (!string.IsNullOrEmpty(typeAttr))
            {
                var aq = XmlQName.Parse(typeAttr, resolve);
                sample = IsBuiltInXsd(aq) ? SampleForBuiltIn(aq.LocalName) : "?";
            }

            sb.Append(' ').Append(name).Append("=\"").Append(EscapeAttr(sample)).Append('"');
        }
    }

    private static bool ComplexTypeYieldsEmptyXmlBody(XElement complexType)
    {
        XNamespace xsd = WsdlNamespaces.Xsd;
        if (EnumerateDeclaredAttributes(complexType).Any())
            return false;

        if (complexType.Element(xsd + "simpleContent") is not null)
            return false;

        var complexContent = complexType.Element(xsd + "complexContent");
        if (complexContent is not null)
        {
            var ext = complexContent.Element(xsd + "extension");
            if (ext is not null)
            {
                if (EnumerateDeclaredAttributes(ext).Any())
                    return false;
                foreach (var child in ext.Elements())
                {
                    if (ParticleHasElementContent(child))
                        return false;
                }

                return true;
            }

            var restriction = complexContent.Element(xsd + "restriction");
            if (restriction is not null)
            {
                if (EnumerateDeclaredAttributes(restriction).Any())
                    return false;
                return !restriction.Elements().Any(ParticleHasElementContent);
            }

            return true;
        }

        foreach (var child in complexType.Elements())
        {
            if (ParticleHasElementContent(child))
                return false;
        }

        return true;
    }

    private static bool ParticleHasElementContent(XElement particle)
    {
        XNamespace xsd = WsdlNamespaces.Xsd;
        if (particle.Name == xsd + "sequence" || particle.Name == xsd + "choice" || particle.Name == xsd + "all")
            return particle.Elements().Any();
        if (particle.Name == xsd + "group")
            return true;
        return false;
    }

    private static IEnumerable<XElement> EnumerateDeclaredAttributes(XElement complexType)
    {
        XNamespace xsd = WsdlNamespaces.Xsd;
        foreach (var at in complexType.Elements(xsd + "attribute"))
            yield return at;
        var ext = complexType.Element(xsd + "complexContent")?.Element(xsd + "extension")
            ?? complexType.Element(xsd + "simpleContent")?.Element(xsd + "extension");
        if (ext is null)
            yield break;
        foreach (var at in ext.Elements(xsd + "attribute"))
            yield return at;
    }

    private static int ParseOccurs(string? v, int def) =>
        string.IsNullOrEmpty(v) ? def : v == "unbounded" ? int.MaxValue : int.TryParse(v, out var n) ? n : def;

    private static bool IsBuiltInXsd(XmlQName q) =>
        q.Namespace == WsdlNamespaces.Xsd && q.LocalName is not (null or "");

    private static string SampleForBuiltIn(string local)
    {
        return local switch
        {
            "string" or "normalizedString" or "token" or "language" or "Name" or "NCName" or "anyURI" => "string",
            "int" or "integer" or "long" or "short" or "byte" or "unsignedInt" or "unsignedLong" or "unsignedShort"
                or "unsignedByte" or "nonPositiveInteger" or "nonNegativeInteger" or "positiveInteger"
                or "negativeInteger" => "0",
            "decimal" or "float" or "double" => "0.0",
            "boolean" => "false",
            "dateTime" => "2000-01-01T00:00:00",
            "date" => "2000-01-01",
            "time" => "00:00:00",
            "base64Binary" => "AA==",
            "hexBinary" => "00",
            "anyType" => "anyType",
            "QName" => "q:name",
            _ => "?"
        };
    }

    private static string SampleForSimpleType(XElement simpleType)
    {
        XNamespace xsd = WsdlNamespaces.Xsd;
        var restriction = simpleType.Element(xsd + "restriction");
        if (restriction is null)
            return "?";
        var baseType = (string?)restriction.Attribute("base");
        if (string.IsNullOrEmpty(baseType))
            return "?";
        var enumVal = restriction.Elements(xsd + "enumeration").Select(e => e.Attribute("value")?.Value)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (!string.IsNullOrEmpty(enumVal))
            return enumVal!;
        var prefix = simpleType.GetPrefixOfNamespace(WsdlNamespaces.Xsd) is { } p && p.Length > 0 ? p + ":" : "s:";
        var bq = XmlQName.Parse(baseType, pre => pre == "s" || pre == "xs" ? WsdlNamespaces.Xsd : "");
        return SampleForBuiltIn(bq.LocalName);
    }

    private static Func<string, string> BuildPrefixResolver(XElement? context)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (context is null)
            return prefix => map.TryGetValue(prefix, out var ns) ? ns : "";

        foreach (var anc in context.AncestorsAndSelf().Reverse())
        {
            foreach (var attr in anc.Attributes())
            {
                if (!attr.IsNamespaceDeclaration)
                    continue;
                var ln = attr.Name.LocalName;
                if (ln == "xmlns")
                    map[""] = attr.Value;
                else
                    map[ln] = attr.Value;
            }
        }

        return prefix => map.TryGetValue(prefix, out var ns) ? ns : "";
    }

    private static string FormatQName(string local, string nsUri, string preferredPrefix)
    {
        if (string.IsNullOrEmpty(nsUri))
            return local;
        return string.IsNullOrEmpty(preferredPrefix) ? local : $"{preferredPrefix}:{local}";
    }

    private static string EscapeQName(XmlQName q, string preferredPrefix)
    {
        if (string.IsNullOrEmpty(q.Namespace))
            return q.LocalName;
        return string.IsNullOrEmpty(preferredPrefix) ? q.LocalName : $"{preferredPrefix}:{q.LocalName}";
    }

    private static string EscapeXml(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string EscapeAttr(string s) =>
        s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
}
