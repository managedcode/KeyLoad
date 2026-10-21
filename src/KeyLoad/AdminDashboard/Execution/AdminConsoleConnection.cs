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
/// <param name="execution">The dedicated execution HTTP/2 listener settings.</param>
/// <param name="sql">The dedicated SQL wire listener settings, when configured.</param>
internal sealed class AdminConsoleConnection(IServer server, IOptions<KeyLoadClientExecutionOptions> options,
    IOptions<ServerExecutionOptions> execution, IOptions<PostgresSessionOptions> sql) : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true })
    {
        MaxResponseContentBufferSize = options.Value.MaximumResponseBodyBytes
    };
    internal KeyLoadClient? Client { get; private set; }
    internal HttpClient Http => http;

    internal void Connect(string credential)
    {
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault(IsHttpListener)
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

    private bool IsHttpListener(string address)
    {
        var port = new UriBuilder(address).Port;
        return (!execution.Value.EnableHttp2 || port != execution.Value.Http2Port)
            && port != sql.Value.Port;
    }

    internal void Disconnect()
    {
        Client = null;
        http.DefaultRequestHeaders.Authorization = null;
    }

    public void Dispose() => http.Dispose();
}
