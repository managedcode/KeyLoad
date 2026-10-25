using System.Globalization;
using System.Net.Security;
using KeyLoad.Clustering;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresEndpointConfiguration(IOptions<PostgresSessionOptions> configured,
    IOptions<GrainRoutingOptions> routing, PostgresRetainedAdmission admission,
    TimeProvider clock) : IStartupFilter
{
    private static readonly SslApplicationProtocol PostgresApplicationProtocol = new(PostgresProtocol.ApplicationProtocol);
    private const string MissingAddressFeatures = "The SQL endpoint requires server address features.";
    private const string BindAnyAddress = "://*:";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => application =>
        {
            var options = configured.Value;
            options.Validate();
            var addresses = application.ServerFeatures.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException(MissingAddressFeatures);
            var scheme = options.RequireTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            addresses.Addresses.Add(string.Create(CultureInfo.InvariantCulture, $"{scheme}{BindAnyAddress}{options.Port}"));
            next(application);
        };

    internal void ConfigureEndpoint(ListenOptions listen)
    {
        var options = configured.Value;
        options.Validate();
        if (listen.IPEndPoint?.Port != options.Port)
        {
            return;
        }
        var duration = routing.Value.RequestLifetime;
        listen.Use(next => context => PostgresAdmissionLifetime.RunAsync(context, next, duration, clock));
        if (options.RequireTls)
        {
            var negotiation = new PostgresSslNegotiation(configured, admission);
            listen.Use(next => connection => negotiation.RunAsync(connection, next));
            listen.UseHttps(tls =>
            {
                tls.HandshakeTimeout = duration;
                // The listener speaks the PostgreSQL protocol, so TLS advertises its ALPN name instead of Kestrel's HTTP
                // protocols; a client that offers "postgresql" is otherwise refused where the platform enforces ALPN.
                tls.OnAuthenticate = (_, authentication) => authentication.ApplicationProtocols = [PostgresApplicationProtocol];
            });
        }
        else
        {
            listen.Use(next => connection => PostgresSslNegotiation.RunTcpAsync(connection, next));
        }
        listen.UseConnectionHandler<PostgresConnectionHandler>();
    }
}
