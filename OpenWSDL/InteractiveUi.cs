using OpenWSDL.OpenApi;
using OpenWSDL.Postman;
using Spectre.Console;

namespace OpenWSDL;

internal static class InteractiveUi
{
    private const string ChoiceOpenApi = "OpenAPI 3 JSON";
    private const string ChoicePostman = "Postman Collection";

    public static async Task<int> RunAsync()
    {
        AnsiConsole.Write(new FigletText("OpenWSDL").Color(Color.Yellow));
        AnsiConsole.MarkupLine("[grey]Tip: run with [bold]--help[/] for non-interactive CLI options.[/]");
        AnsiConsole.Write(new Rule("[italic grey]Interactive mode[/]"));

        var urlString = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]WSDL URL[/] [grey](https://…)[/]:")
                .PromptStyle(Color.White)
                .ValidationErrorMessage("[red]Must be an absolute http(s) URL[/]")
                .Validate(s =>
                {
                    if (!Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u))
                        return ValidationResult.Error("Invalid URL");
                    if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
                        return ValidationResult.Error("Use http or https");
                    return ValidationResult.Success();
                }));

        var wsdlUri = new Uri(urlString.Trim());

        ConversionContext? ctx = null;
        var exitCode = 0;
        string? loadError = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow bold"))
            .StartAsync("[yellow]Fetching WSDL and schemas…[/]", async _ =>
            {
                var (c, code, err) = await WsdlConversionPipeline.TryBuildAsync(wsdlUri).ConfigureAwait(false);
                ctx = c;
                exitCode = code;
                loadError = err;
            })
            .ConfigureAwait(false);

        if (ctx is null)
        {
            if (exitCode == 2)
            {
                AnsiConsole.MarkupLine(
                    "[red]No SOAP 1.1/1.2 bindings with resolvable messages were found in this WSDL.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Failed to load WSDL:[/] " + Markup.Escape(loadError ?? "Unknown error"));
            }

            return exitCode == 0 ? 3 : exitCode;
        }

        AnsiConsole.MarkupLine(
            "[green]OK[/] — [bold]" + Markup.Escape(ctx.DefaultTitle) + "[/] — [cyan]" + ctx.Operations.Count +
            "[/] SOAP operation(s).");

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[green]Which outputs[/] do you want to generate? [grey](space to toggle, enter to confirm)[/]")
                .PageSize(5)
                .HighlightStyle(new Style(foreground: Color.Black, background: Color.Yellow))
                .InstructionsText("[grey]([blue]<space>[/] toggle, [green]<enter>[/] accept)[/]")
                .AddChoices(ChoiceOpenApi, ChoicePostman));

        if (selected.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Select at least one output type.[/]");
            return 1;
        }

        var wantOpenApi = selected.Contains(ChoiceOpenApi);
        var wantPostman = selected.Contains(ChoicePostman);

        var outDirInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Output folder[/]:")
                .DefaultValue(Directory.GetCurrentDirectory())
                .PromptStyle(Color.White)
                .ValidationErrorMessage("[red]Invalid path[/]")
                .Validate(p =>
                {
                    try
                    {
                        _ = Path.GetFullPath(p.Trim());
                        return ValidationResult.Success();
                    }
                    catch
                    {
                        return ValidationResult.Error("Invalid path");
                    }
                }));

        var outDir = Path.GetFullPath(outDirInput.Trim());
        if (!Directory.Exists(outDir))
        {
            if (!AnsiConsole.Confirm($"[yellow]Folder does not exist.[/] Create [bold]{Markup.Escape(outDir)}[/]?"))
            {
                AnsiConsole.MarkupLine("[red]Output folder is required.[/]");
                return 1;
            }

            Directory.CreateDirectory(outDir);
        }

        var baseDefault = SanitizeFileName(ctx.DefaultTitle);
        var baseName = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Base file name[/] [grey](no extension; used for .openapi.json / .postman_collection.json)[/]:")
                .DefaultValue(baseDefault)
                .PromptStyle(Color.White)
                .ValidationErrorMessage("[red]Invalid file name[/]")
                .Validate(n =>
                {
                    var t = SanitizeFileName(n.Trim());
                    return string.IsNullOrEmpty(t)
                        ? ValidationResult.Error("Enter a non-empty name")
                        : ValidationResult.Success();
                }));

        baseName = SanitizeFileName(baseName.Trim());

        var title = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]API / collection title[/] [grey](OpenAPI info.title + Postman info.name)[/]:")
                .DefaultValue(ctx.DefaultTitle)
                .AllowEmpty());

        var displayTitle = string.IsNullOrWhiteSpace(title) ? ctx.DefaultTitle : title.Trim();

        string? openApiPath = wantOpenApi ? Path.Combine(outDir, $"{baseName}.openapi.json") : null;
        string? postmanPath = wantPostman ? Path.Combine(outDir, $"{baseName}.postman_collection.json") : null;

        var preview = new Table().Border(TableBorder.Rounded).Title("[bold]Files to write[/]");
        preview.AddColumn("[grey]Kind[/]");
        preview.AddColumn("[grey]Path[/]");
        if (wantOpenApi)
            preview.AddRow("[cyan]OpenAPI[/]", Markup.Escape(openApiPath!));
        if (wantPostman)
            preview.AddRow("[magenta]Postman[/]", Markup.Escape(postmanPath!));
        AnsiConsole.Write(preview);

        if (!AnsiConsole.Confirm("[green]Generate these files?[/]", true))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            return 0;
        }

        await OutputWriter.WriteAsync(ctx, displayTitle, openApiPath, postmanPath, Console.WriteLine)
            .ConfigureAwait(false);

        AnsiConsole.Write(
            new Panel("[green]Done.[/] Import the Postman file via [bold]Import[/] in Postman if you generated one.")
                .Header("[yellow]Success[/]")
                .BorderColor(Color.Green));

        return 0;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim();
        return string.IsNullOrEmpty(s) ? "service" : s;
    }
}
