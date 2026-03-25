using System.Xml.Linq;
using OpenWSDL;

namespace OpenWSDL.Wsdl;

internal sealed record SoapOperationDescriptor(
    string Name,
    string SoapAction,
    bool IsSoap12,
    string BindingStyle,
    XmlQName BodyElementQName,
    string ServiceLocation,
    string TargetNamespace,
    string? PreferredBodyPrefix);

/// <summary>WSDL <c>wsdl:service</c> name (if present) and extracted SOAP operations.</summary>
internal sealed record SoapServiceExtraction(string? ServiceName, IReadOnlyList<SoapOperationDescriptor> Operations);

internal static class WsdlInterpreter
{
    public static SoapServiceExtraction ExtractSoapService(IReadOnlyDictionary<Uri, XDocument> documents, Uri entryUri)
    {
        XNamespace wsdl = WsdlNamespaces.Wsdl;
        var results = new List<SoapOperationDescriptor>();

        foreach (var uri in OrderDocuments(documents, entryUri))
        {
            if (!documents.TryGetValue(uri, out var doc))
                continue;

            var root = doc.Root;
            if (root is null || root.Name != wsdl + "definitions")
                continue;

            var tns = (string?)root.Attribute("targetNamespace") ?? "";
            var nsMap = BuildNsMap(root);

            foreach (var service in root.Elements(wsdl + "service"))
            {
                var serviceName = (string?)service.Attribute("name");
                foreach (var port in service.Elements(wsdl + "port"))
                {
                    var soapAddr = port.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "address" &&
                        (e.Name.NamespaceName == WsdlNamespaces.Soap11 ||
                         e.Name.NamespaceName == WsdlNamespaces.Soap12));

                    if (soapAddr is null)
                        continue;

                    var location = (string?)soapAddr.Attribute("location");
                    if (string.IsNullOrWhiteSpace(location))
                        continue;

                    var bindingRef = (string?)port.Attribute("binding");
                    if (string.IsNullOrWhiteSpace(bindingRef))
                        continue;

                    var bindingQ = XmlQName.Parse(bindingRef, p => nsMap.TryGetValue(p, out var u) ? u : "");
                    var binding = FindBinding(root, documents, bindingQ);
                    if (binding is null)
                        continue;

                    var isSoap12 = soapAddr.Name.NamespaceName == WsdlNamespaces.Soap12;
                    var typeRef = (string?)binding.Attribute("type");
                    if (string.IsNullOrWhiteSpace(typeRef))
                        continue;
                    var portTypeQ = XmlQName.Parse(typeRef, p => nsMap.TryGetValue(p, out var u) ? u : "");
                    var portType = FindPortType(root, documents, portTypeQ);
                    if (portType is null)
                        continue;

                    var portTypeDefinitions = portType.Document?.Root;
                    if (portTypeDefinitions is null || portTypeDefinitions.Name != wsdl + "definitions")
                        continue;
                    var portTypeNsMap = BuildNsMap(portTypeDefinitions);

                    var soapBinding = binding.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "binding" &&
                        (e.Name.NamespaceName == WsdlNamespaces.Soap11 ||
                         e.Name.NamespaceName == WsdlNamespaces.Soap12));
                    var style = (string?)soapBinding?.Attribute("style") ?? "document";

                    foreach (var bop in binding.Elements(wsdl + "operation"))
                    {
                        var opName = (string?)bop.Attribute("name");
                        if (string.IsNullOrEmpty(opName))
                            continue;

                        var soapOp = bop.Elements().FirstOrDefault(e =>
                            e.Name.LocalName == "operation" &&
                            (e.Name.NamespaceName == WsdlNamespaces.Soap11 ||
                             e.Name.NamespaceName == WsdlNamespaces.Soap12));
                        var soapAction = (string?)soapOp?.Attribute("soapAction") ?? "";

                        var portOp = portType.Elements(wsdl + "operation")
                            .FirstOrDefault(o => (string?)o.Attribute("name") == opName);
                        if (portOp is null)
                            continue;

                        var input = portOp.Element(wsdl + "input");
                        var inputMsg = ResolveMessageQName(input, portTypeNsMap);
                        if (inputMsg.IsEmpty)
                            continue;

                        var message = FindMessage(root, documents, inputMsg);
                        if (message is null)
                            continue;

                        var messageDefinitions = message.Document?.Root ?? portTypeDefinitions;
                        var messageNsMap = messageDefinitions.Name == wsdl + "definitions"
                            ? BuildNsMap(messageDefinitions)
                            : portTypeNsMap;

                        var bodyEl = ResolveBodyElementQName(message, style, messageNsMap);
                        if (bodyEl.IsEmpty)
                            continue;

                        var bodyPrefix = GuessPrefixForNamespace(root, documents, bodyEl.Namespace);
                        results.Add(new SoapOperationDescriptor(
                            opName,
                            soapAction,
                            isSoap12,
                            style,
                            bodyEl,
                            location,
                            tns,
                            bodyPrefix));
                    }

                    if (results.Count > 0)
                        return new SoapServiceExtraction(serviceName, results);
                }
            }
        }

        return new SoapServiceExtraction(null, results);
    }

    private static IEnumerable<Uri> OrderDocuments(IReadOnlyDictionary<Uri, XDocument> documents, Uri entryUri)
    {
        var ordered = documents.Keys.OrderBy(u => u.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
        var entryMatch = ordered.FirstOrDefault(u => UriLikelySame(u, entryUri));
        var seen = new HashSet<Uri>();
        if (entryMatch is not null)
        {
            yield return entryMatch;
            seen.Add(entryMatch);
        }

        foreach (var u in ordered)
        {
            if (seen.Add(u))
                yield return u;
        }
    }

    private static bool UriLikelySame(Uri a, Uri b)
    {
        var pa = a.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var pb = b.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GuessPrefixForNamespace(XElement definitions,
        IReadOnlyDictionary<Uri, XDocument> documents, string nsUri)
    {
        if (string.IsNullOrEmpty(nsUri))
            return null;

        foreach (var doc in documents.Values)
        {
            var root = doc.Root;
            if (root is null)
                continue;
            foreach (var attr in root.Attributes())
            {
                if (!attr.IsNamespaceDeclaration)
                    continue;
                if (attr.Value != nsUri)
                    continue;
                var ln = attr.Name.LocalName;
                return ln is "xmlns" ? null : ln;
            }

            foreach (var sch in root.Descendants().Where(d => d.Name.LocalName == "schema"))
            {
                foreach (var attr in sch.Attributes())
                {
                    if (!attr.IsNamespaceDeclaration || attr.Value != nsUri)
                        continue;
                    var ln = attr.Name.LocalName;
                    return ln is "xmlns" ? null : ln;
                }
            }
        }

        return "tns";
    }

    private static XmlQName ResolveBodyElementQName(XElement message, string style,
        Dictionary<string, string> nsMap)
    {
        XNamespace wsdl = WsdlNamespaces.Wsdl;
        var parts = message.Elements(wsdl + "part").ToList();
        if (parts.Count == 0)
            return default;

        var first = parts[0];
        var element = (string?)first.Attribute("element");
        if (!string.IsNullOrEmpty(element))
            return XmlQName.Parse(element, p => nsMap.TryGetValue(p, out var u) ? u : "");

        var typeName = (string?)first.Attribute("type");
        var partName = (string?)first.Attribute("name") ?? "parameters";
        if (string.Equals(style, "rpc", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(typeName))
        {
            var tq = XmlQName.Parse(typeName, p => nsMap.TryGetValue(p, out var u) ? u : "");
            return tq;
        }

        return new XmlQName("", partName);
    }

    private static XmlQName ResolveMessageQName(XElement? input, Dictionary<string, string> nsMap)
    {
        if (input is null)
            return default;
        var msg = (string?)input.Attribute("message");
        if (string.IsNullOrEmpty(msg))
            return default;
        return XmlQName.Parse(msg, p => nsMap.TryGetValue(p, out var u) ? u : "");
    }

    private static XElement? FindBinding(XElement definitions, IReadOnlyDictionary<Uri, XDocument> documents,
        XmlQName q)
    {
        return FindInDefinitions(definitions, documents, q, "binding");
    }

    private static XElement? FindPortType(XElement definitions, IReadOnlyDictionary<Uri, XDocument> documents,
        XmlQName q)
    {
        return FindInDefinitions(definitions, documents, q, "portType");
    }

    private static XElement? FindMessage(XElement definitions, IReadOnlyDictionary<Uri, XDocument> documents,
        XmlQName q)
    {
        return FindInDefinitions(definitions, documents, q, "message");
    }

    private static XElement? FindInDefinitions(XElement definitions, IReadOnlyDictionary<Uri, XDocument> documents,
        XmlQName q, string localName)
    {
        XNamespace wsdl = WsdlNamespaces.Wsdl;
        foreach (var doc in documents.Values)
        {
            var root = doc.Root;
            if (root is null || root.Name != wsdl + "definitions")
                continue;
            var tns = (string?)root.Attribute("targetNamespace") ?? "";
            if (tns != q.Namespace)
                continue;
            var found = root.Elements(wsdl + localName)
                .FirstOrDefault(e => (string?)e.Attribute("name") == q.LocalName);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static Dictionary<string, string> BuildNsMap(XElement root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in root.Attributes())
        {
            if (!attr.IsNamespaceDeclaration)
                continue;
            var ln = attr.Name.LocalName;
            if (ln == "xmlns")
                map[""] = attr.Value;
            else
                map[ln] = attr.Value;
        }

        return map;
    }
}
