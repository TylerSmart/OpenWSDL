using OpenWSDL.Wsdl;

namespace OpenWSDL;

internal static class WsdlConversionPipeline
{
    public static async Task<(ConversionContext? Context, int ExitCode, string? ErrorMessage)> TryBuildAsync(Uri wsdlUrl)
    {
        try
        {
            using var http = WsdlHttpClient.Create();

            var loader = new WsdlLoader(http);
            var documents = await loader.LoadAllAsync(wsdlUrl).ConfigureAwait(false);

            var index = new SchemaIndex();
            index.IndexDocuments(documents.Values);

            var extraction = WsdlInterpreter.ExtractSoapService(documents, wsdlUrl);
            var operations = extraction.Operations;
            if (operations.Count == 0)
                return (null, 2, null);

            var sampleGen = new SampleXmlGenerator(index);
            var examples = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var op in operations)
            {
                var qualified = index.UsesQualifiedElementForm(op.BodyElementQName);
                var inner = sampleGen.GenerateElementTree(op.BodyElementQName, SoapEnvelopeBuilder.ContractSamplePrefix,
                    qualified);
                var envelope = SoapEnvelopeBuilder.Build(op, inner);
                examples[op.Name] = XmlPrettyFormatter.Indent(envelope);
            }

            var defaultTitle = !string.IsNullOrWhiteSpace(extraction.ServiceName)
                ? extraction.ServiceName!
                : new Uri(operations[0].ServiceLocation).Host;

            return (new ConversionContext
            {
                Extraction = extraction,
                Operations = operations,
                Examples = examples,
                DefaultTitle = defaultTitle,
            }, 0, null);
        }
        catch (Exception ex)
        {
            return (null, 3, ex.Message);
        }
    }
}
