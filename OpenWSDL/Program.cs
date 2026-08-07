using System.CommandLine;
using System.CommandLine.Invocation;
using OpenWSDL;

if (args.Length == 0)
    return await InteractiveUi.RunAsync();

var wsdlArg = new Argument<string?>("wsdl-url", "WSDL URL (http/https). Omit to enter when the program starts.")
{
    Arity = ArgumentArity.ZeroOrOne,
};
var outOpt = new Option<string?>(new[] { "--output", "-o" }, "Output OpenAPI 3 JSON file path");
var postmanOpt = new Option<string?>(new[] { "--postman", "-p" },
    "Output Postman Collection v2.1 JSON (one request per SOAP operation, headers preset). " +
    "Optional path; defaults to {service-name}.postman_collection.json in the current directory.")
{
    Arity = ArgumentArity.ZeroOrOne,
};
var titleOpt = new Option<string?>("--title", "API / collection title (default: WSDL service name, or host)");

var root = new RootCommand(
    "Generate OpenAPI 3 and/or a Postman Collection from a WSDL URL. " +
    "Use --postman for one request per SOAP method with Content-Type and SOAPAction set. " +
    "Run with no arguments for the interactive UI.")
{
    wsdlArg,
    outOpt,
    postmanOpt,
    titleOpt,
};

root.SetHandler(async (InvocationContext context) =>
    {
        var parseResult = context.ParseResult;
        var wsdlUrl = parseResult.GetValueForArgument(wsdlArg);
        var output = parseResult.GetValueForOption(outOpt);
        var postman = parseResult.GetValueForOption(postmanOpt);
        var title = parseResult.GetValueForOption(titleOpt);
        var postmanRequested = parseResult.FindResultFor(postmanOpt) is not null;

        context.ExitCode = await RunCliAsync(wsdlUrl, output, postman, postmanRequested, title);
    });

return await root.InvokeAsync(args);

static async Task<int> RunCliAsync(
    string? wsdlUrlFromArgs,
    string? outputPath,
    string? postmanPath,
    bool postmanRequested,
    string? title)
{
    var wsdlUrl = ResolveWsdlUri(wsdlUrlFromArgs);
    if (wsdlUrl is null)
        return 1;

    var (ctx, exitCode, errorMessage) = await WsdlConversionPipeline.TryBuildAsync(wsdlUrl).ConfigureAwait(false);
    if (ctx is null)
    {
        if (exitCode == 2)
            Console.Error.WriteLine("No SOAP 1.1/1.2 bindings with resolvable messages were found.");
        else
            Console.Error.WriteLine("Failed to load WSDL: {0}", errorMessage ?? "Unknown error");
        return exitCode == 0 ? 3 : exitCode;
    }

    var displayTitle = string.IsNullOrWhiteSpace(title) ? ctx.DefaultTitle : title.Trim();

    if (postmanRequested && string.IsNullOrWhiteSpace(postmanPath))
        postmanPath = FileNameHelper.DefaultPostmanPath(displayTitle);

    var writeOpenApi = !string.IsNullOrWhiteSpace(outputPath);
    var writePostman = !string.IsNullOrWhiteSpace(postmanPath);

    await OutputWriter.WriteAsync(ctx, displayTitle,
            writeOpenApi ? outputPath : null,
            writePostman ? postmanPath : null,
            Console.WriteLine)
        .ConfigureAwait(false);

    return 0;
}

static Uri? ResolveWsdlUri(string? fromArgs)
{
    var s = fromArgs?.Trim();
    if (string.IsNullOrEmpty(s))
    {
        Console.Write("WSDL URL: ");
        s = Console.ReadLine()?.Trim();
    }

    if (string.IsNullOrEmpty(s))
    {
        Console.Error.WriteLine("A WSDL URL is required.");
        return null;
    }

    if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        Console.Error.WriteLine("Invalid URL. Use an absolute http or https URL.");
        return null;
    }

    return uri;
}
