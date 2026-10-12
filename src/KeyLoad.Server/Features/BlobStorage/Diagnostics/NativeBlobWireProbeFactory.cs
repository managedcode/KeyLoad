using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireProbeFactory
{
    internal const string FixedRoot = "/native-blob-wire-probes";

    internal static NativeBlobWireProbeObserver? Create(IServiceProvider services,
        PartitionHost partition, NodeOptions options)
    {
        var selected = options.NativeBlobWireProbe;
        if (selected.Root is null && selected.SessionId is null) { return null; }
        if (!options.RequestCqrsProbe.Enabled || selected.Root != FixedRoot
            || selected.SessionId != options.RequestCqrsProbe.SessionId
            || !RequestCqrsProbeOptionsReader.IsSessionId(selected.SessionId)
            || options.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Control)
        { throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidOwner); }
        RequestCqrsProbePaths.RequireDirectory(selected.Root, FixedRoot);
        var limits = services.GetRequiredService<IOptions<RequestProbeExecutionOptions>>();
        limits.Value.Validate();
        var codec = new NativeBlobWireCodec(limits);
        var shape = new NativeBlobWireShape(limits);
        var files = new NativeBlobWireFiles(selected.Root, limits);
        var state = new NativeBlobWireState(files, codec, shape, services.GetRequiredService<IOptions<NodeOptions>>(), partition);
        var claims = new NativeBlobWireClaims(services.GetRequiredService<IOptions<OrleansMembershipOptions>>(), shape);
        return new(new(files, codec, state, claims), new(files, codec, limits), new(files, codec, limits));
    }
}
