using System.Net.Http;

namespace OpenWSDL;

/// <summary>
/// Builds <see cref="HttpClient"/> instances suitable for WSDL retrieval, including
/// Windows integrated auth (NTLM / Negotiate) when the server responds with 401.
/// </summary>
internal static class WsdlHttpClient
{
    /// <summary>
    /// Creates a client that sends <see cref="CredentialCache.DefaultCredentials"/> for
    /// authentication challenges (typical for IIS and corporate endpoints).
    /// </summary>
    public static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            // Current Windows user (or process identity) for NTLM/Kerberos after 401 challenge.
            UseDefaultCredentials = true,
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWSDL/1.0 (+https://github.com)");
        return client;
    }
}
