using OpenWSDL.Wsdl;

namespace OpenWSDL;

internal sealed class ConversionContext
{
    public required SoapServiceExtraction Extraction { get; init; }
    public required IReadOnlyList<SoapOperationDescriptor> Operations { get; init; }
    public required Dictionary<string, string> Examples { get; init; }
    public required string DefaultTitle { get; init; }
}
