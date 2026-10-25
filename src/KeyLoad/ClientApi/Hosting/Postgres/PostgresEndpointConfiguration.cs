using System.Net.Security;
using KeyLoad.Clustering;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresEndpointConfiguration(IOptions<PostgresSessionOptions> configured,
    IOptions<GrainRoutingOptions> routing, PostgresServerCertificate certificate, PostgresRetainedAdmission admission,
    TimeProvider clock) : IConfigureOptions<KestrelServerOptions>
{
    private static readonly SslApplicationProtocol PostgresApplicationProtocol = new(PostgresProtocol.ApplicationProtocol);

    public void Configure(KestrelServerOptions server)
    {
        var options = configured.Value;
        options.Validate();
        server.ListenAnyIP(options.Port, listen =>
        {
            var duration = routing.Value.RequestLifetime;
            listen.Use(next => context => PostgresAdmissionLifetime.RunAsync(context, next, duration, clock));
            var negotiation = new PostgresSslNegotiation(configured, admission);
            listen.Use(next => connection => negotiation.RunAsync(connection, next));
            listen.UseHttps(certificate.Value, tls =>
            {
                tls.HandshakeTimeout = duration;
                // The listener speaks the PostgreSQL protocol, so TLS advertises its ALPN name instead of Kestrel's HTTP
                // protocols; a client that offers "postgresql" is otherwise refused where the platform enforces ALPN.
                tls.OnAuthenticate = (_, authentication) => authentication.ApplicationProtocols = [PostgresApplicationProtocol];
            });
            listen.UseConnectionHandler<PostgresConnectionHandler>();
        });
    }
}
