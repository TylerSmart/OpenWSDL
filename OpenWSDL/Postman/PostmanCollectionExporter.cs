using System.Text.Json;
using System.Text.Json.Serialization;
using OpenWSDL.Wsdl;

namespace OpenWSDL.Postman;

internal static class PostmanCollectionExporter
{
    private const string CollectionSchema =
        "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";

    public static string BuildJson(
        string collectionName,
        IReadOnlyList<SoapOperationDescriptor> operations,
        IReadOnlyDictionary<string, string> bodyByOperationName)
    {
        var baseUrl = operations.FirstOrDefault()?.ServiceLocation ?? "http://localhost";
        var baseUri = new Uri(baseUrl);
        var baseUrlValue = $"{baseUri.Scheme}://{baseUri.Authority}";

        var items = operations.Select(op =>
        {
            if (!bodyByOperationName.TryGetValue(op.Name, out var raw))
                raw = "<!-- sample not generated -->";
            return BuildItem(op, raw);
        }).ToList();

        var root = new PostmanCollectionRoot
        {
            Info = new PostmanInfo
            {
                PostmanId = Guid.NewGuid().ToString("N"),
                Name = collectionName,
                Schema = CollectionSchema,
            },
            Item = items,
            Variable = new List<PostmanVariable>
            {
                new()
                {
                    Key = "baseUrl",
                    Value = baseUrlValue,
                    Type = "string",
                },
            },
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    private static PostmanItem BuildItem(SoapOperationDescriptor op, string rawXml)
    {
        var headers = new List<PostmanHeader>();
        if (op.IsSoap12)
        {
            var action = ResolveSoapActionUri(op);
            headers.Add(new PostmanHeader
            {
                Key = "Content-Type",
                Value = $"application/soap+xml; charset=utf-8; action=\"{action}\"",
                Type = "text",
            });
        }
        else
        {
            headers.Add(new PostmanHeader
            {
                Key = "Content-Type",
                Value = "text/xml; charset=utf-8",
                Type = "text",
            });
            headers.Add(new PostmanHeader
            {
                Key = "SOAPAction",
                Value = SoapActionHeaderValueQuoted(op),
                Type = "text",
                Description =
                    "Quoted action URI (common for .NET BasicHttpBinding). Remove the outer quotes if your server expects an unquoted value.",
            });
        }

        return new PostmanItem
        {
            Name = op.Name,
            Request = new PostmanRequest
            {
                Method = "POST",
                Header = headers,
                Body = new PostmanBody
                {
                    Mode = "raw",
                    Raw = rawXml,
                    Options = new PostmanBodyOptions { Raw = new PostmanRawLanguage { Language = "xml" } },
                },
                Url = BuildUrl(op.ServiceLocation),
            },
        };
    }

    private static string ResolveSoapActionUri(SoapOperationDescriptor op)
    {
        if (!string.IsNullOrEmpty(op.SoapAction))
            return op.SoapAction;
        var t = op.TargetNamespace.TrimEnd('/');
        return $"{t}/{op.Name}";
    }

    /// <summary>SOAP 1.1 SOAPAction header value including double quotes, as many stacks send on the wire.</summary>
    private static string SoapActionHeaderValueQuoted(SoapOperationDescriptor op)
    {
        var uri = ResolveSoapActionUri(op);
        return $"\"{uri}\"";
    }

    private static PostmanUrl BuildUrl(string serviceLocation)
    {
        var u = new Uri(serviceLocation.Trim());
        var path = u.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

        var pathStr = path.Count > 0 ? "/" + string.Join("/", path) : "";
        var rawUrl = $"{{{{baseUrl}}}}{pathStr}";

        return new PostmanUrl
        {
            Raw = rawUrl,
            Protocol = null,
            Host = new List<string> { "{{baseUrl}}" },
            Path = path,
            Port = null,
        };
    }

    private sealed class PostmanCollectionRoot
    {
        public PostmanInfo Info { get; set; } = null!;
        public List<PostmanItem> Item { get; set; } = new();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<PostmanVariable>? Variable { get; set; }
    }

    private sealed class PostmanInfo
    {
        [JsonPropertyName("_postman_id")]
        public string PostmanId { get; set; } = "";

        public string Name { get; set; } = "";
        public string Schema { get; set; } = "";
    }

    private sealed class PostmanItem
    {
        public string Name { get; set; } = "";
        public PostmanRequest Request { get; set; } = null!;
    }

    private sealed class PostmanRequest
    {
        public string Method { get; set; } = "POST";
        public List<PostmanHeader> Header { get; set; } = new();
        public PostmanBody Body { get; set; } = null!;
        public PostmanUrl Url { get; set; } = null!;
    }

    private sealed class PostmanHeader
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string Type { get; set; } = "text";

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }
    }

    private sealed class PostmanBody
    {
        public string Mode { get; set; } = "raw";
        public string Raw { get; set; } = "";
        public PostmanBodyOptions? Options { get; set; }
    }

    private sealed class PostmanBodyOptions
    {
        public PostmanRawLanguage Raw { get; set; } = new();
    }

    private sealed class PostmanRawLanguage
    {
        public string Language { get; set; } = "xml";
    }

    private sealed class PostmanUrl
    {
        public string Raw { get; set; } = "";

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Protocol { get; set; }

        public List<string> Host { get; set; } = new();
        public List<string> Path { get; set; } = new();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Port { get; set; }
    }

    private sealed class PostmanVariable
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string Type { get; set; } = "string";
    }
}
