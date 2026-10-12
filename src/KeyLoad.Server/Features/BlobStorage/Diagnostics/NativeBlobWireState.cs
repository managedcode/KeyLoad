using Microsoft.Extensions.Options;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireState
{
    private readonly Lock gate = new();
    private readonly NativeBlobWireFiles files;
    private readonly NativeBlobWireCodec codec;
    private readonly NativeBlobWireShape shape;
    private readonly IOptions<NodeOptions> options;
    private readonly PartitionHost partition;
    private readonly string ownerDigest;
    private string? armsDigest;

    internal NativeBlobWireState(NativeBlobWireFiles files, NativeBlobWireCodec codec,
        NativeBlobWireShape shape, IOptions<NodeOptions> options, PartitionHost partition)
    {
        this.files = files; this.codec = codec; this.shape = shape;
        this.options = options; this.partition = partition;
        var bytes = files.Read(NativeBlobWireProbeProtocol.OwnerFile);
        ownerDigest = NativeBlobWireCodec.Digest(bytes);
        RequireOwner(codec.Read<NativeBlobWireOwner>(bytes));
    }

    internal NativeBlobWireSnapshot Read()
    {
        lock (gate)
        {
            var bytes = files.Read(NativeBlobWireProbeProtocol.OwnerFile);
            if (NativeBlobWireCodec.Digest(bytes) != ownerDigest) { throw Invalid(); }
            var owner = codec.Read<NativeBlobWireOwner>(bytes);
            RequireOwner(owner);
            if (!files.Exists(NativeBlobWireProbeProtocol.ArmsFile))
            {
                if (armsDigest is not null) { throw Invalid(); }
                return new(owner, ownerDigest, null, null);
            }
            var armed = files.Read(NativeBlobWireProbeProtocol.ArmsFile);
            var digest = NativeBlobWireCodec.Digest(armed);
            if (armsDigest is not null && armsDigest != digest) { throw Invalid(); }
            var arms = codec.Read<NativeBlobWireArms>(armed);
            shape.RequireArms(owner, ownerDigest, arms);
            armsDigest = digest;
            return new(owner, ownerDigest, arms, digest);
        }
    }

    private void RequireOwner(NativeBlobWireOwner owner)
    {
        shape.RequireOwner(owner);
        var options = this.options.Value;
        if (owner.SessionId != options.NativeBlobWireProbe.SessionId || owner.ClusterId != options.ClusterId
            || !NativeBlobWireShape.Same(owner.SourceOwner, PhysicalOwnerConfiguredTuples.Local(options, partition).Owner)
            || !NativeBlobWireShape.Same(owner.ControlOwner, PhysicalOwnerConfiguredTuples.Control(options, partition).Owner)
            || !NativeBlobWireShape.Same(owner.DestinationOwner, PhysicalOwnerConfiguredTuples.Destination(options, partition).Owner))
        { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidOwner);
}
