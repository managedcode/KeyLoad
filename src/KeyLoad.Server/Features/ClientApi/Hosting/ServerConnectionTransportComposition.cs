using System.Globalization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Adds explicit native HTTP/2 while retaining the original configured listener bindings.</summary>
[ConfigurationBinding]
internal static class ServerConnectionTransportComposition
{
    private const string KestrelSection = "Kestrel";
    private const string EndpointsSection = "Endpoints";
    private const string Http2Endpoint = "http2";
    private const string UrlSetting = "Url";
    private const string ProtocolsSetting = "Protocols";
    private const string PreservedEndpointPrefix = "original-url-";
    private const string DefaultServerAddress = "http://localhost:5000";
    private const string HttpPortFormat = "http://*:{0}";
    private const string HttpsPortFormat = "https://*:{0}";
    private const string Http2AddressFormat = "http://0.0.0.0:{0}";
    private const char UrlSeparator = ';';
    private const int InitialEndpointIndex = 0;
    private static readonly System.Text.CompositeFormat Http2Address = System.Text.CompositeFormat.Parse(Http2AddressFormat);
    private static readonly System.Text.CompositeFormat HttpAddress = System.Text.CompositeFormat.Parse(HttpPortFormat);
    private static readonly System.Text.CompositeFormat HttpsAddress = System.Text.CompositeFormat.Parse(HttpsPortFormat);

    internal static void Configure(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var configuration = builder.Configuration;
        builder.Services.AddOptions<KestrelServerOptions>().Configure<IOptions<ServerExecutionOptions>>((server, configured) =>
        {
            var execution = configured.Value;
            if (!execution.EnableHttp2)
            { return; }
            var endpoints = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (!configuration.GetSection(KestrelSection).GetSection(EndpointsSection).GetChildren().Any())
            { PreserveOriginalListeners(configuration, endpoints); }
            endpoints[EndpointKey(Http2Endpoint, UrlSetting)] = string.Format(CultureInfo.InvariantCulture,
                Http2Address, execution.Http2Port);
            endpoints[EndpointKey(Http2Endpoint, ProtocolsSetting)] = HttpProtocols.Http2.ToString();
            configuration.AddInMemoryCollection(endpoints);
            server.Configure(configuration.GetSection(KestrelSection));
        });
    }

    private static void PreserveOriginalListeners(ConfigurationManager configuration, Dictionary<string, string?> endpoints)
    {
        var addresses = configuration[WebHostDefaults.ServerUrlsKey];
        var urls = !string.IsNullOrWhiteSpace(addresses)
            ? addresses.Split(UrlSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ReadPortListeners(configuration);
        var index = InitialEndpointIndex;
        foreach (var url in urls)
        {
            var name = PreservedEndpointPrefix + index.ToString(CultureInfo.InvariantCulture);
            endpoints[EndpointKey(name, UrlSetting)] = url;
            index++;
        }
    }

    private static string[] ReadPortListeners(ConfigurationManager configuration)
    {
        var listeners = new List<string>();
        AddPorts(listeners, configuration[WebHostDefaults.HttpPortsKey], HttpAddress);
        AddPorts(listeners, configuration[WebHostDefaults.HttpsPortsKey], HttpsAddress);
        return listeners.Count == InitialEndpointIndex ? [DefaultServerAddress] : listeners.ToArray();
    }

    private static void AddPorts(List<string> listeners, string? configuredPorts, System.Text.CompositeFormat format)
    {
        if (string.IsNullOrWhiteSpace(configuredPorts))
        { return; }
        foreach (var port in configuredPorts.Split(UrlSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        { listeners.Add(string.Format(CultureInfo.InvariantCulture, format, port)); }
    }

    private static string EndpointKey(string name, string setting)
        => string.Join(ConfigurationPath.KeyDelimiter, KestrelSection, EndpointsSection, name, setting);
}
