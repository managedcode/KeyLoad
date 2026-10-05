using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeObserverFactory
{
    internal static RequestCqrsProbeObserver? Create(RequestCqrsProbeOptions options, ReplicaConfiguration replica,
        bool allowPrivateNetworkHttp, ILocalSiloDetails localSilo, IHostApplicationLifetime applicationLifetime)
    {
        ArgumentNullException.ThrowIfNull(replica);
        RequestCqrsProbeOptionsReader.Validate(options, replica, allowPrivateNetworkHttp);
        if (!options.Enabled)
        { return null; }
        ArgumentNullException.ThrowIfNull(localSilo);
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        return RequestCqrsProbeObserver.Create(options, replica, localSilo, applicationLifetime);
    }
}
