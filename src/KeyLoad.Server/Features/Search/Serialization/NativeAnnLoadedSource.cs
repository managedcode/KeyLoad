using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed record NativeAnnLoadedSource(NativeAnnManifest Manifest, PackedAnnIndex Index, AnnSeed Seed);

internal static class NativeAnnLoadedSources
{
    private const int MinimumBytes = 1024;
    private const int EmptyExaminedRecords = 0;

    internal static NativeAnnLoadedSource Load(string root, NativeAnnPointer pointer,
        AnnMaintenanceRequest request, AnnSeed current, IOptions<PackedAnnOptions> policy,
        IOptions<PackedAnnStorageOptions> storage, NativeAnnExecutionOptions options,
        AnnSeedOptions seeds, long maximumPeak, AnnWorkBudget indexBudget, ReadExecutionBudget readBudget)
    {
        seeds.Validate();
        if (maximumPeak < MinimumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        var bounded = NativeAnnStorageOptionsFactory.ForPeak(storage, Math.Min(maximumPeak, seeds.MaxPeakBytes));
        var loaded = NativeAnnGenerationFiles.Load(root, pointer, request, current, policy, bounded, options, indexBudget);
        RequireCheckpoint(loaded.Manifest, current);
        if (loaded.Index.Count > seeds.MaxRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        var snapshotPeak = Math.Min(bounded.Value.MaxPeakBytes, checked(loaded.Index.RetainedBytesUpperBound
            + seeds.MaxOwnedBytes - seeds.HashScratchBytes));
        var snapshotStorage = NativeAnnStorageOptionsFactory.ForPeak(bounded, snapshotPeak);
        var snapshot = loaded.Index.Snapshot(loaded.Manifest.Field, snapshotStorage, indexBudget);
        var owned = checked(snapshot.OwnedBytesUpperBound + seeds.HashScratchBytes);
        var peak = checked(owned + loaded.Index.RetainedBytesUpperBound);
        if (snapshot.Records.Length > seeds.MaxRecords || owned > seeds.MaxOwnedBytes
            || peak > seeds.MaxPeakBytes || peak > maximumPeak)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        var work = new AnnSeedWork(readBudget, seeds.MaxWorkUnits);
        var scratch = new byte[seeds.HashScratchBytes];
        var manifest = loaded.Manifest;
        var scope = current.Scope with { PolicyEpoch = manifest.Source.PolicyEpoch, SchemaVersion = manifest.Source.SchemaVersion };
        var digest = AnnSeedFingerprint.Compute(scope, snapshot.Records, work, scratch);
        if (digest != manifest.Source.CorpusSha256)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        var source = manifest.Source;
        var cut = new AnnSeedCut(source.NodeId, source.Incarnation, source.StoreFormatVersion,
            source.KeyCodecVersion, source.ReadGeneration, source.Position, source.AppliedPosition,
            source.ThroughSequence, source.OutboxFirstAvailable);
        var seed = new AnnSeed(scope, cut, snapshot.Records, digest, owned, peak, EmptyExaminedRecords, work.Units)
        { DependencySha256 = source.DependencySha256, ProjectionCheckpoint = source.ThroughSequence };
        return new(manifest, loaded.Index, seed);
    }

    private static void RequireCheckpoint(NativeAnnManifest manifest, AnnSeed current)
    {
        if (current.ProjectionCheckpoint != manifest.Source.ThroughSequence)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
        var intent = manifest.CheckpointIntent;
        if (intent is null || intent.Consumer != manifest.Consumer || !intent.Effects.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }
}
