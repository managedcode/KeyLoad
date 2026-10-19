using System.Net;
using System.Net.Http.Headers;
using KeyLoad.Client;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns the local authenticated HTTP transport for one administration circuit.</summary>
/// <param name="server">The actual local listener.</param>
/// <param name="options">Validated SDK transport bounds.</param>
internal sealed class AdminConsoleConnection(IServer server, IOptions<KeyLoadClientExecutionOptions> options) : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true })
    {
        MaxResponseContentBufferSize = options.Value.MaximumResponseBodyBytes
    };
    internal KeyLoadClient? Client { get; private set; }
    internal HttpClient Http => http;

    internal void Connect(string credential)
    {
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException(AdminConsoleText.NoAddress);
        var target = new UriBuilder(address);
        if (target.Host is AdminConsoleText.AnyIpv6 or AdminConsoleText.BareIpv6 or AdminConsoleText.AnyIpv4 or AdminConsoleText.Wildcard or AdminConsoleText.AllHosts)
        {
            target.Host = IPAddress.Loopback.ToString();
        }
        // The destination comes from Kestrel, never the browser's Host header or form input.
        http.BaseAddress ??= target.Uri;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(AdminConsoleText.Bearer, credential);
        Client = new KeyLoadClient(http, credential, options);
    }

    internal void Disconnect()
    {
        Client = null;
        http.DefaultRequestHeaders.Authorization = null;
    }

    public void Dispose() => http.Dispose();
}
