using OpenWSDL.OpenApi;
using OpenWSDL.Postman;

namespace OpenWSDL;

internal static class OutputWriter
{
    public static async Task WriteAsync(
        ConversionContext ctx,
        string displayTitle,
        string? openApiPath,
        string? postmanPath,
        Action<string>? logLine)
    {
        var writeOpenApi = !string.IsNullOrWhiteSpace(openApiPath);
        var writePostman = !string.IsNullOrWhiteSpace(postmanPath);

        if (writePostman)
        {
            var postmanJson = PostmanCollectionExporter.BuildJson(displayTitle, ctx.Operations, ctx.Examples);
            await File.WriteAllTextAsync(postmanPath!, postmanJson).ConfigureAwait(false);
            logLine?.Invoke($"Wrote Postman collection: {Path.GetFullPath(postmanPath!)}");
        }

        if (writeOpenApi)
        {
            var openApi = OpenApiSoapExporter.Build(displayTitle, ctx.Operations, ctx.Examples);
            var json = OpenApiSoapExporter.SerializeJson(openApi);
            await File.WriteAllTextAsync(openApiPath!, json).ConfigureAwait(false);
            logLine?.Invoke($"Wrote OpenAPI: {Path.GetFullPath(openApiPath!)}");
        }
        else if (!writePostman)
        {
            var openApi = OpenApiSoapExporter.Build(displayTitle, ctx.Operations, ctx.Examples);
            Console.Out.WriteLine(OpenApiSoapExporter.SerializeJson(openApi));
        }
    }
}
