using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeObserverFactory
{
    internal static RequestCqrsProbeObserver? Create(RequestCqrsProbeOptions options, IOptions<ReplicaConfiguration> replicaOptions,
        bool allowPrivateNetworkHttp, ILocalSiloDetails localSilo, IHostApplicationLifetime applicationLifetime,
        IOptions<RequestProbeExecutionOptions> executionOptions, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(replicaOptions);
        RequestCqrsProbeOptionsReader.Validate(options, replicaOptions.Value, allowPrivateNetworkHttp);
        if (!options.Enabled)
        { return null; }
        ArgumentNullException.ThrowIfNull(localSilo);
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        var address = localSilo.SiloAddress.ToParsableString();
        var files = RequestCqrsProbeFiles.Open(options, replicaOptions, executionOptions);
        return new(files, replicaOptions, address, applicationLifetime, executionOptions, clock);
    }
}
