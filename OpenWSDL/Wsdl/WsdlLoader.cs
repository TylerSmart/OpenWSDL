using System.Xml.Linq;

namespace OpenWSDL.Wsdl;

internal sealed class WsdlLoader
{
    private readonly HttpClient _http;
    private readonly Dictionary<Uri, XDocument> _documents = new();
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

    public WsdlLoader(HttpClient http) => _http = http;

    public async Task<IReadOnlyDictionary<Uri, XDocument>> LoadAllAsync(Uri entryUri,
        CancellationToken cancellationToken = default)
    {
        _documents.Clear();
        _visited.Clear();
        await LoadRecursiveAsync(entryUri, cancellationToken).ConfigureAwait(false);
        return _documents;
    }

    private async Task LoadRecursiveAsync(Uri uri, CancellationToken cancellationToken)
    {
        var key = uri.ToString();
        if (!_visited.Add(key))
            return;

        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        _documents[uri] = doc;

        var baseUri = uri;
        XNamespace wsdl = WsdlNamespaces.Wsdl;
        XNamespace xsd = WsdlNamespaces.Xsd;

        foreach (var imp in doc.Descendants(wsdl + "import"))
        {
            var loc = (string?)imp.Attribute("location");
            if (string.IsNullOrWhiteSpace(loc))
                continue;
            if (!Uri.TryCreate(baseUri, loc, out var next))
                continue;
            await LoadRecursiveAsync(next, cancellationToken).ConfigureAwait(false);
        }

        foreach (var sch in doc.Descendants(xsd + "schema"))
        {
            foreach (var imp in sch.Elements(xsd + "import"))
            {
                var schemaLoc = (string?)imp.Attribute("schemaLocation");
                if (string.IsNullOrWhiteSpace(schemaLoc))
                    continue;
                if (!Uri.TryCreate(baseUri, schemaLoc, out var next))
                    continue;
                await LoadRecursiveAsync(next, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
