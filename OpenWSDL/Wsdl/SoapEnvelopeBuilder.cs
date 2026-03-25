using System.Text;
using OpenWSDL;

namespace OpenWSDL.Wsdl;

internal static class SoapEnvelopeBuilder
{
    /// <summary>Prefix used on the SOAP envelope (SoapUI-style).</summary>
    private const string Soap11EnvPrefix = "soapenv";

    /// <summary>Prefix for the contract/body element namespace in samples.</summary>
    public const string ContractSamplePrefix = "ns";

    public static string Build(SoapOperationDescriptor op, string innerBodyXml)
    {
        return op.IsSoap12 ? BuildSoap12(op, innerBodyXml) : BuildSoap11(op, innerBodyXml);
    }

    private static string BuildSoap11(SoapOperationDescriptor op, string innerBodyXml)
    {
        var sb = new StringBuilder();
        sb.Append('<').Append(Soap11EnvPrefix).Append(":Envelope xmlns:").Append(Soap11EnvPrefix).Append("=\"")
            .Append(WsdlNamespaces.SoapEnv11).Append('"');
        AppendContractNs(sb, op);
        sb.Append('>');
        sb.Append('<').Append(Soap11EnvPrefix).Append(":Header/>");
        sb.Append('<').Append(Soap11EnvPrefix).Append(":Body>");
        sb.Append(innerBodyXml);
        sb.Append("</").Append(Soap11EnvPrefix).Append(":Body>");
        sb.Append("</").Append(Soap11EnvPrefix).Append(":Envelope>");
        return sb.ToString();
    }

    private static string BuildSoap12(SoapOperationDescriptor op, string innerBodyXml)
    {
        const string p = "soap12";
        var sb = new StringBuilder();
        sb.Append('<').Append(p).Append(":Envelope xmlns:").Append(p).Append("=\"")
            .Append(WsdlNamespaces.SoapEnv12).Append('"');
        AppendContractNs(sb, op);
        sb.Append('>');
        sb.Append('<').Append(p).Append(":Header/>");
        sb.Append('<').Append(p).Append(":Body>");
        sb.Append(innerBodyXml);
        sb.Append("</").Append(p).Append(":Body>");
        sb.Append("</").Append(p).Append(":Envelope>");
        return sb.ToString();
    }

    private static void AppendContractNs(StringBuilder sb, SoapOperationDescriptor op)
    {
        if (string.IsNullOrEmpty(op.BodyElementQName.Namespace))
            return;

        sb.Append(" xmlns:").Append(ContractSamplePrefix).Append("=\"")
            .Append(EscapeAttrNs(op.BodyElementQName.Namespace)).Append('"');
    }

    private static string EscapeAttrNs(string s) =>
        s.Replace("&", "&amp;").Replace("\"", "&quot;");
}
