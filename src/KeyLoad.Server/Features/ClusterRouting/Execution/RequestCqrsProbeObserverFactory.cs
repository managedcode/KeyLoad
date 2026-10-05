using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeObserverFactory
{
    internal static RequestCqrsProbeObserver? Create(RequestCqrsProbeOptions options, IOptions<ReplicaConfiguration> replicaOptions,
        bool allowPrivateNetworkHttp, ILocalSiloDetails localSilo, IHostApplicationLifetime applicationLifetime,
        IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(replicaOptions);
        RequestCqrsProbeOptionsReader.Validate(options, replicaOptions.Value, allowPrivateNetworkHttp);
        if (!options.Enabled)
        { return null; }
        ArgumentNullException.ThrowIfNull(localSilo);
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        return RequestCqrsProbeObserver.Create(options, replicaOptions, localSilo, applicationLifetime, executionOptions);
    }
}
