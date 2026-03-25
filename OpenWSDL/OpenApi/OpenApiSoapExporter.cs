using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using OpenWSDL.Wsdl;

namespace OpenWSDL.OpenApi;

internal static class OpenApiSoapExporter
{
    public static OpenApiDocument Build(
        string title,
        IReadOnlyList<SoapOperationDescriptor> operations,
        IReadOnlyDictionary<string, string> exampleEnvelopeByOperation)
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = title,
                Version = "1.0.0",
                Description =
                    "Converted from WSDL. For **Postman**, prefer exporting a **Postman Collection** (`--postman`) so you get one request per SOAP operation with headers filled in. " +
                    "This OpenAPI file uses **one POST** with **examples** (OpenAPI cannot duplicate the same path/method). " +
                    "Request body is **text/plain** so imports do not wrap XML in an extra element; use **Content-Type: text/xml; charset=utf-8** (see header parameter).",
            },
            Paths = new OpenApiPaths(),
        };

        if (operations.Count == 0)
            return doc;

        var serviceUrl = operations[0].ServiceLocation.Trim();
        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var endpoint))
            return doc;

        var origin = $"{endpoint.Scheme}://{endpoint.Authority}";
        var path = string.IsNullOrEmpty(endpoint.AbsolutePath) ? "/" : endpoint.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        doc.Servers = new List<OpenApiServer> { new() { Url = origin } };

        var exampleMap = new Dictionary<string, OpenApiExample>(StringComparer.Ordinal);
        foreach (var op in operations)
        {
            if (!exampleEnvelopeByOperation.TryGetValue(op.Name, out var xml))
                xml = "<!-- sample not generated -->";

            var action = SoapActionFor(op);
            exampleMap[SanitizeExampleKey(op.Name)] = new OpenApiExample
            {
                Summary = op.Name,
                Description =
                    "Set header **SOAPAction** to this value for this body (SOAP 1.1 often expects quotes in the actual HTTP header): `" +
                    action + "`",
                Value = new OpenApiString(xml),
            };
        }

        var requestMedia = new OpenApiMediaType
        {
            Schema = new OpenApiSchema { Type = "string" },
            Examples = exampleMap,
        };

        var openOp = new OpenApiOperation
        {
            OperationId = "soapEndpoint",
            Summary = "SOAP POST (choose an example + matching SOAPAction)",
            Description =
                "Each **example** is one WSDL operation. Use that example’s XML body and the **SOAPAction** shown in its description.",
            Tags = new List<OpenApiTag> { new() { Name = "SOAP" } },
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "SOAP response (XML)",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["text/xml"] = new()
                        {
                            Schema = new OpenApiSchema { Type = "string" },
                        },
                    },
                },
            },
            RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description =
                    "SOAP XML payload. Media type is **text/plain** in this spec so imports (e.g. Postman) keep the body as raw text. " +
                    "Send it with **Content-Type: text/xml** (see header parameter).",
                Content = new Dictionary<string, OpenApiMediaType> { ["text/plain"] = requestMedia },
            },
        };

        openOp.Parameters = BuildSoapParameters(operations);

        var pathItem = new OpenApiPathItem();
        pathItem.AddOperation(OperationType.Post, openOp);
        doc.Paths.Add(path, pathItem);

        return doc;
    }

    public static string SerializeJson(OpenApiDocument document)
    {
        using var sw = new StringWriter();
        var writer = new OpenApiJsonWriter(sw);
        document.SerializeAsV3(writer);
        return sw.ToString();
    }

    private static string SoapActionFor(SoapOperationDescriptor op)
    {
        if (!string.IsNullOrEmpty(op.SoapAction))
            return op.SoapAction;
        return $"{op.TargetNamespace}{(op.TargetNamespace.EndsWith('/') ? "" : "/")}{op.Name}";
    }

    private static List<OpenApiParameter>? BuildSoapParameters(IReadOnlyList<SoapOperationDescriptor> operations)
    {
        var list = new List<OpenApiParameter>();
        var all12 = operations.All(o => o.IsSoap12);
        var all11 = operations.All(o => !o.IsSoap12);

        if (all11)
        {
            list.Add(new OpenApiParameter
            {
                Name = "Content-Type",
                In = ParameterLocation.Header,
                Required = true,
                Schema = new OpenApiSchema { Type = "string" },
                Example = new OpenApiString("text/xml; charset=utf-8"),
                Description =
                    "SOAP 1.1 entity body (include **charset=utf-8** as needed). The OpenAPI body is `text/plain` only so tools do not rewrite the XML.",
            });
            list.Add(new OpenApiParameter
            {
                Name = "SOAPAction",
                In = ParameterLocation.Header,
                Required = false,
                Schema = new OpenApiSchema { Type = "string" },
                Example = new OpenApiString($"\"{SoapActionFor(operations[0])}\""),
                Description =
                    "Must match the **example** you selected (see that example’s description). Value shown with double quotes as many .NET stacks expect on the wire.",
            });
        }
        else if (all12 && !string.IsNullOrEmpty(operations[0].SoapAction))
        {
            list.Add(new OpenApiParameter
            {
                Name = "Content-Type",
                In = ParameterLocation.Header,
                Required = true,
                Schema = new OpenApiSchema { Type = "string" },
                Example = new OpenApiString(
                    $"application/soap+xml; charset=utf-8; action=\"{operations[0].SoapAction}\""),
                Description = "SOAP 1.2: adjust **action** to match the example you use.",
            });
        }

        return list.Count > 0 ? list : null;
    }

    private static string SanitizeExampleKey(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var s = new string(chars).Trim('_');
        return string.IsNullOrEmpty(s) ? "operation" : s;
    }
}
