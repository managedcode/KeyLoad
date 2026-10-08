using System.Collections.Immutable;
using System.Runtime.InteropServices;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnPendingCodec
{
    private const int Empty = 0;
    private const int ReferenceBytes = 8;
    private const long RecordBytes = 64;
    private const int TransientChunks = 3;

    internal static byte[] Save(Stream file, NativeAnnPendingReplay pending,
        PackedAnnStorageOptions storage, AnnWorkBudget budget)
    {
        RequireHeader(pending, pending.Records.Length);
        PackedAnnStorageCodec.RequirePeak(checked(TransientChunks * PackedAnnStorageFrames.MaximumChunkBytes), storage);
        PackedAnnStorageFrames.Write(file, pending with { Records = [] }, storage, budget);
        foreach (var row in pending.Records)
        { budget.Check(); PackedAnnStorageFrames.Write(file, row, storage, budget); }
        return PackedAnnStorageFrames.Digest(file, storage, budget);
    }

    internal static NativeAnnReplaySource Load(Stream file, NativeAnnManifest manifest, AnnSeed current,
        PackedAnnStorageOptions storage, AnnSeedOptions options, AnnWorkBudget indexBudget, ReadExecutionBudget readBudget)
    {
        PackedAnnStorageFrames.RequireDigest(file, manifest.IndexSha256, storage, indexBudget);
        file.Position = Empty;
        var header = PackedAnnStorageFrames.Read<NativeAnnPendingReplay>(file, indexBudget);
        RequireHeader(header, manifest.Count);
        RequireManifestFields(header, manifest);
        var rows = ReadRows(file, manifest, storage, options, indexBudget, out var owned);
        if (file.Position != file.Length)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        var work = new AnnSeedWork(readBudget, options.MaxWorkUnits);
        var digest = AnnSeedFingerprint.Compute(current.Scope, rows, work, new byte[options.HashScratchBytes]);
        if (digest != header.CorpusSha256 || digest != manifest.ReplayCorpusSha256)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        PackedAnnStorageFrames.RequireDigest(file, manifest.IndexSha256, storage, indexBudget);
        return new(current.Scope, current.DependencySha256!, rows, owned, header.ThroughSequence);
    }

    private static ImmutableArray<VectorRecord> ReadRows(Stream file, NativeAnnManifest manifest,
        PackedAnnStorageOptions storage, AnnSeedOptions options, AnnWorkBudget budget, out long owned)
    {
        if (manifest.Count < Empty || manifest.Count > options.MaxRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        owned = AnnSeedAccounting.ArrayAllowance(ReferenceBytes, manifest.Count);
        RequirePeak(owned, storage, options);
        var rows = new VectorRecord[manifest.Count];
        string? previous = null;
        for (var index = Empty; index < rows.Length; index++)
        {
            RequirePeak(owned, storage, options);
            var row = PackedAnnStorageFrames.Read<VectorRecord>(file, budget);
            RequireRow(row, previous, manifest);
            owned = checked(owned + RecordBytes + AnnSeedAccounting.StringAllowance(row.DocumentId)
                + AnnSeedAccounting.ArrayAllowance(sizeof(float), row.Values.Length));
            RequirePeak(owned, storage, options);
            previous = row.DocumentId;
            rows[index] = row;
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(rows);
    }

    private static void RequirePeak(long owned, PackedAnnStorageOptions storage, AnnSeedOptions options)
    {
        var peak = checked(owned + options.HashScratchBytes + TransientChunks * PackedAnnStorageFrames.MaximumChunkBytes);
        if (owned > options.MaxOwnedBytes || peak > options.MaxPeakBytes || peak > storage.MaxPeakBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
    }

    private static void RequireRow(VectorRecord row, string? previous, NativeAnnManifest manifest)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.DocumentId) || row.Field != manifest.Field
            || row.Space != manifest.Space || row.Values.IsDefault || row.Values.Length != manifest.Space.Dimension
            || row.DocumentRevision <= Empty || previous is not null && StringComparer.Ordinal.Compare(previous, row.DocumentId) >= Empty
            || row.Values.Any(value => !float.IsFinite(value)))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }

    private static void RequireHeader(NativeAnnPendingReplay header, int count)
    {
        if (header is null || header.Version != NativeAnnProtocol.Version || header.AdmittedUpper is null || header.AdmittedUpper.Source is null
            || count < Empty || header.AfterSequence < Empty || header.ThroughSequence <= header.AfterSequence
            || header.ThroughSequence > header.AdmittedUpper.Source.ThroughSequence
            || header.CheckpointIntent is null || header.CheckpointIntent.CommandId == Guid.Empty
            || header.CheckpointIntent.Consumer != header.AdmittedUpper.Consumer || header.CheckpointIntent.Effects.IsDefault
            || !header.CheckpointIntent.Effects.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }

    private static void RequireManifestFields(NativeAnnPendingReplay header, NativeAnnManifest manifest)
    {
        if (!manifest.IsPending || header.Records.IsDefault || !header.Records.IsEmpty || header.AdmittedUpper.Source != manifest.Source
            || header.AdmittedUpper.Consumer != manifest.Consumer || header.AdmittedUpper.IndexGeneration != manifest.IndexGeneration
            || header.AdmittedUpper.PrincipalId != manifest.PrincipalId || header.AdmittedUpper.Collection != manifest.Collection
            || header.AdmittedUpper.Field != manifest.Field || header.AdmittedUpper.Space != manifest.Space
            || header.AdmittedUpper.Policy != manifest.Policy
            || header.AdmittedUpper.ExplicitBuildStage != manifest.ExplicitBuildStage
            || !NativeSerialization.Serialize(header.AdmittedUpper.Placement).AsSpan().SequenceEqual(NativeSerialization.Serialize(manifest.Placement))
            || header.AfterSequence != manifest.ReplayAfter || header.ThroughSequence != manifest.ReplayThrough
            || NativeSerialization.Serialize(header.CheckpointIntent).AsSpan().SequenceEqual(
                NativeSerialization.Serialize(manifest.CheckpointIntent)) is false)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }
}
