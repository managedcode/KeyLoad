using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresEndpointConfiguration(IOptions<PostgresSessionOptions> configured,
    PostgresServerCertificate certificate, PostgresRetainedAdmission admission,
    IOptions<DatabaseLimits> limits, TimeProvider clock) : IConfigureOptions<KestrelServerOptions>
{
    public void Configure(KestrelServerOptions server)
    {
        var options = configured.Value;
        options.Validate();
        server.ListenAnyIP(options.Port, listen =>
        {
            var duration = TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
            listen.Use(next => context => PostgresAdmissionLifetime.RunAsync(context, next, duration, clock));
            var negotiation = new PostgresSslNegotiation(configured, admission);
            listen.Use(next => connection => negotiation.RunAsync(connection, next));
            listen.UseHttps(certificate.Value, tls => tls.HandshakeTimeout = duration);
            listen.UseConnectionHandler<PostgresConnectionHandler>();
        });
    }
}
