using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaSnapshotUpgradeValidation
{
    internal const string MissingState = "A persisted replica hard state is required for snapshot conversion.";
    internal const string InvalidAuthority = "The replica snapshot conversion scope changed after preflight.";

    internal static void Revalidate(ReplicaSnapshotUpgradePlan plan, DatabaseEngine canonical,
        IAtomicStore replica, ReplicaConfiguration configuration)
    {
        var state = ReadHardState(replica, configuration);
        if (!MatchesConfiguration(plan.Configuration, configuration) || plan.Canonical != Bind(canonical.Store)
            || plan.Replica != Bind(replica) || plan.HardState != state.State
            || !string.Equals(plan.HardStateSha256, state.Sha256, StringComparison.Ordinal)
            || plan.CanonicalAppliedPosition != ReadAppliedPosition(canonical.Store))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidAuthority); }
        var current = ReplicaSnapshotUpgradeInventory.Read(plan.SourceSnapshots, configuration,
            plan.HardState.Snapshot, path => SourceCut(plan, path));
        if (current.Images.Count != plan.Images.Length
            || current.Images.Where((image, index) => image != plan.Images[index]).Any())
        { throw Errors.Fail(ErrorCode.Conflict, InvalidAuthority); }
    }

    internal static ReplicaUpgradeStoreBinding Bind(IAtomicStore store)
    {
        var identity = store.Identity;
        return new(identity.FormatVersion, identity.KeyCodecVersion, identity.NodeId, identity.Incarnation,
            Convert.ToHexStringLower(SHA256.HashData(identity.SigningKey.Span)), identity.Durability,
            identity.DispatchPaused, identity.ReadGeneration, store.Position);
    }

    internal static ReplicaUpgradeConfigurationBinding Bind(ReplicaConfiguration configuration)
        => new(configuration.LocalId, ImmutableArray.CreateRange(configuration.VoterIds), Path.GetFullPath(configuration.Directory),
            configuration.Incarnation, configuration.BenchmarkTopology, configuration.SnapshotThreshold,
            configuration.LowerElectionTimeout.Ticks, configuration.UpperElectionTimeout.Ticks,
            configuration.HeartbeatInterval.Ticks, configuration.RpcTimeout.Ticks,
            configuration.MaxAppendEntries, configuration.MaxAppendBytes, configuration.SnapshotChunkBytes,
            configuration.MaxSnapshotBytes);

    private static bool MatchesConfiguration(ReplicaUpgradeConfigurationBinding binding,
        ReplicaConfiguration configuration)
        => binding.LocalId == configuration.LocalId
            && binding.VoterIds.SequenceEqual(configuration.VoterIds, StringComparer.Ordinal)
            && binding.Directory == Path.GetFullPath(configuration.Directory)
            && binding.Incarnation == configuration.Incarnation
            && binding.BenchmarkTopology == configuration.BenchmarkTopology
            && binding.SnapshotThreshold == configuration.SnapshotThreshold
            && binding.LowerElectionTicks == configuration.LowerElectionTimeout.Ticks
            && binding.UpperElectionTicks == configuration.UpperElectionTimeout.Ticks
            && binding.HeartbeatTicks == configuration.HeartbeatInterval.Ticks
            && binding.RpcTicks == configuration.RpcTimeout.Ticks
            && binding.MaxAppendEntries == configuration.MaxAppendEntries
            && binding.MaxAppendBytes == configuration.MaxAppendBytes
            && binding.SnapshotChunkBytes == configuration.SnapshotChunkBytes
            && binding.MaxSnapshotBytes == configuration.MaxSnapshotBytes;

    internal static (ReplicaHardState State, string Sha256) ReadHardState(IAtomicStore replica,
        ReplicaConfiguration configuration)
    {
        var bytes = replica.Read(view => view.ReadOwnedValue(ReplicaProtocol.StateStorageKey))
            ?? throw Errors.Fail(ErrorCode.FormatUnsupported, MissingState);
        return (ReplicaProtocolCodec.DeserializeStored<ReplicaHardState>(bytes, configuration.MaxAppendEntries),
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    internal static long ReadAppliedPosition(IAtomicStore store)
    {
        try
        { return ReplicaPersistence.AppliedPosition(store); }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog); }
    }

    internal static void ValidateNodeScopes(ReplicaUpgradeStoreBinding canonical,
        ReplicaUpgradeStoreBinding replica, ReplicaConfiguration configuration)
    {
        if (canonical.Incarnation != configuration.Incarnation || replica.Incarnation != configuration.Incarnation
            || canonical.NodeId == replica.NodeId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidAuthority); }
    }

    internal static void ValidateAppliedCut(long applied, ReplicaHardState state)
    {
        if (applied < (state.Snapshot?.Index ?? 0) || applied > state.CommittedIndex)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaPersistence.CanonicalAhead); }
    }

    internal static void ValidatePointerCut(DurableReplicaLog log, ReplicaSnapshot? pointer,
        List<ReplicaSnapshotUpgradeImage> images, long applied)
    {
        if (pointer is null)
        { return; }
        var image = images.Single(candidate => candidate.FileName == pointer.FileName);
        if (pointer.Index > applied || pointer.Index > log.State.CommittedIndex || log.TermAt(pointer.Index) != pointer.Term
            || image.Snapshot.AppliedPosition != pointer.Index)
        { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot); }
    }

    internal static void ValidateImageCuts(List<ReplicaSnapshotUpgradeImage> images, ReplicaHardState state,
        long applied)
    {
        if (images.Any(image => image.Snapshot.AppliedPosition > applied
            || image.Snapshot.AppliedPosition > state.CommittedIndex))
        { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot); }
    }

    private static StorageSnapshot SourceCut(ReplicaSnapshotUpgradePlan plan, string path)
    {
        var image = plan.Images.SingleOrDefault(candidate => candidate.FileName == Path.GetFileName(path));
        return image?.Snapshot ?? throw Errors.Fail(ErrorCode.FormatUnsupported, ReplicaProtocol.InvalidSnapshot);
    }
}
