using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnStorageCodec
{
    private const int Empty = 0;
    private const int ReferenceBytes = 8;
    private const int TransientFrameCount = 2;

    internal static byte[] Save(PackedAnnState state, Stream destination,
        IOptions<PackedAnnStorageOptions> configured, AnnWorkBudget budget)
    {
        var options = configured.Value;
        options.Validate();
        budget.Check();
        if (!destination.CanRead || !destination.CanWrite || !destination.CanSeek
            || destination.Position != Empty || destination.Length != Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, PackedAnnStorageFrames.InvalidSnapshot);
        }
        RequirePeak(checked(state.RetainedBytesUpperBound + PackedAnnStorageFrames.MaximumChunkBytes), options);
        var header = new PackedAnnStorageHeader(PackedAnnStorageFrames.CurrentVersion, state.Space,
            state.Options, state.Count, state.EntryPoint, state.MaximumLevel,
            state.RetainedBytesUpperBound, state.BuildScratchBytesUpperBound);
        PackedAnnStorageFrames.Write(destination, header, options, budget);
        for (var ordinal = Empty; ordinal < state.Count; ordinal++)
        {
            budget.Check();
            var nodeBytes = PackedAnnStorageExport.Reservation(state, ordinal, budget);
            RequirePeak(checked(state.RetainedBytesUpperBound + nodeBytes
                + TransientFrameCount * PackedAnnStorageFrames.MaximumChunkBytes), options);
            PackedAnnStorageFrames.Write(destination, PackedAnnStorageExport.Node(state, ordinal, budget), options, budget);
        }
        destination.Flush();
        return PackedAnnStorageFrames.Digest(destination, options, budget);
    }

    internal static PackedAnnState Load(Stream source, byte[] digest, IOptions<PackedAnnOptions> configuredPolicy,
        IOptions<PackedAnnStorageOptions> configuredStorage, AnnWorkBudget budget)
    {
        var policy = configuredPolicy.Value;
        policy.Validate();
        var storage = configuredStorage.Value;
        storage.Validate();
        budget.Check();
        if (digest is null || digest.Length != PackedAnnStorageFrames.DigestBytes)
        { throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot); }
        var admittedDigest = digest.ToArray();
        PackedAnnStorageFrames.RequireDigest(source, admittedDigest, storage, budget);
        source.Position = Empty;
        var header = PackedAnnStorageFrames.Read<PackedAnnStorageHeader>(source, budget);
        PackedAnnStorageValidation.Header(header, policy, budget);
        var nodes = ReadNodes(source, header, storage, budget, out var snapshotBytes);
        if (source.Position != source.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
        }
        PackedAnnStorageValidation.Topology(header, nodes, budget);
        var restored = PackedAnnStorageRestore.State(header, nodes, configuredPolicy, storage, snapshotBytes, budget);
        PackedAnnStorageFrames.RequireDigest(source, admittedDigest, storage, budget);
        budget.Check();
        return restored;
    }

    private static PackedAnnStorageNode[] ReadNodes(Stream source, PackedAnnStorageHeader header,
        PackedAnnStorageOptions storage, AnnWorkBudget budget, out long snapshotBytes)
    {
        snapshotBytes = PackedAnnReservations.Array(ReferenceBytes, header.Count);
        RequirePeak(checked(snapshotBytes + PackedAnnStorageFrames.MaximumChunkBytes), storage);
        var nodes = new PackedAnnStorageNode[header.Count];
        string? previous = null;
        for (var ordinal = Empty; ordinal < nodes.Length; ordinal++)
        {
            budget.Check();
            var node = PackedAnnStorageFrames.Read<PackedAnnStorageNode>(source, budget);
            snapshotBytes = checked(snapshotBytes + PackedAnnStorageValidation.Node(node, header, ordinal, previous, budget));
            RequirePeak(checked(snapshotBytes + PackedAnnStorageFrames.MaximumChunkBytes), storage);
            nodes[ordinal] = node;
            previous = node.Id;
        }
        return nodes;
    }

    internal static void RequirePeak(long bytes, PackedAnnStorageOptions storage)
    {
        if (bytes > storage.MaxPeakBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PackedAnnStorageFrames.SnapshotExceeded);
        }
    }
}
