using System.Runtime.InteropServices;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnReplay
{
    private const int Empty = 0;
    private const int DictionaryEntryBytes = 128;
    private const int ReferenceBytes = 8;
    private const long RowBytes = 64;
    private readonly NativeAnnReplaySource initial;
    private readonly NativeAnnOwnedKey owner;
    private readonly bool applyDeltas;
    private readonly AnnSeedOptions options;
    private readonly AnnSeedWork work;
    private readonly ReadExecutionBudget originalBudget;
    private readonly Dictionary<string, VectorRecord> records = new(StringComparer.Ordinal);
    private readonly Dictionary<long, (long Through, byte[] Digest)> appliedPages = [];
    private long retained;
    private long peak;
    private long through;

    internal NativeAnnReplay(NativeAnnReplaySource seed, NativeAnnOwnedKey owner, IOptions<AnnSeedOptions> configured, ReadExecutionBudget budget, bool applyDeltas = true)
    {
        var options = configured.Value;
        options.Validate();
        initial = seed;
        this.owner = owner;
        this.applyDeltas = applyDeltas;
        this.options = options;
        originalBudget = budget;
        work = new(budget, options.MaxWorkUnits);
        through = seed.ThroughSequence;
        retained = checked(seed.OwnedBytesUpperBound + AnnSeedAccounting.ArrayAllowance(DictionaryEntryBytes, seed.Records.Length));
        Reserve(retained);
        records.EnsureCapacity(seed.Records.Length);
        foreach (var record in seed.Records)
        { work.Charge(); records.Add(record.DocumentId, record); }
    }

    internal long WorkUnits => work.Units;
    internal IDisposable EnterStageCancellation(CancellationToken token) => originalBudget.EnterStageCancellation(token);
    internal long Through => through;
    internal long RetainedBytes => retained;
    internal long MaximumStagePeakBytes => options.MaxPeakBytes;

    internal void Apply(ProjectionBatch batch, long upper)
    {
        work.Check();
        if (batch.Consumer.Consumer != owner.Consumer
            || batch.Consumer.Definition.IndexGeneration != owner.IndexGeneration)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.Stale); }
        using var encoded = new NativeAnnHashStream(work, options.MaxPeakBytes);
        NativeSerialization.Serialize(new NativeAnnReplayPage(batch.Consumer, batch.ThroughSequence, batch.Entries), encoded);
        var digest = encoded.Finish();
        if (appliedPages.TryGetValue(batch.Consumer.Checkpoint, out var applied))
        {
            if (applied.Through != batch.ThroughSequence)
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
            NativeAnnDigest.Require(applied.Digest, digest);
            return;
        }
        if (batch.Consumer.Released || batch.Consumer.Checkpoint != through
            || batch.ThroughSequence < through || batch.ThroughSequence > upper)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.Stale); }
        var previous = through;
        foreach (var entry in batch.Entries)
        {
            work.Charge();
            if (entry.Sequence <= previous || entry.Sequence > batch.ThroughSequence
                || entry.Commit.AtomicPartitionId != initial.Scope.Partition.AtomicPartitionId)
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
            if (applyDeltas)
            { ApplyEntry(entry); }
            previous = entry.Sequence;
        }
        retained = checked(retained + DictionaryEntryBytes + NativeAnnDigest.Bytes);
        Reserve(retained);
        appliedPages.Add(batch.Consumer.Checkpoint, (batch.ThroughSequence, digest));
        through = batch.ThroughSequence;
    }

    private void ApplyEntry(OutboxEntry entry)
    {
        switch (entry.Mutation)
        {
            case PutDocument put when put.Collection == initial.Scope.Collection:
                records.Remove(put.Id);
                break;
            case PatchDocument patch when patch.Collection == initial.Scope.Collection:
                records.Remove(patch.Id);
                break;
            case DeleteDocument delete when delete.Collection == initial.Scope.Collection:
                records.Remove(delete.Id);
                break;
            case PutVector vector:
                ApplyVector(vector, entry.Receipt);
                break;
            case ApplyVectorProjection projection:
                ApplyVector(projection.Target, entry.Receipt);
                break;
        }
    }

    private void ApplyVector(PutVector vector, MutationReceipt receipt)
    {
        if (vector.Collection != initial.Scope.Collection || vector.Field != initial.Scope.Field)
        { return; }
        records.Remove(vector.Id);
        if (vector.Space != initial.Scope.Space)
        { return; }
        if (receipt.Resource != vector.Collection || receipt.Id != vector.Id
            || receipt.Revision != vector.ExpectedDocumentRevision || vector.Values.IsDefault
            || vector.Values.Length != vector.Space.Dimension)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        if (records.Count == options.MaxRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.RecordLimit); }
        var bytes = checked(RowBytes + AnnSeedAccounting.StringAllowance(vector.Id)
            + AnnSeedAccounting.ArrayAllowance(sizeof(float), vector.Values.Length) + DictionaryEntryBytes);
        retained = checked(retained + bytes);
        Reserve(retained);
        var values = new float[vector.Values.Length];
        for (var index = Empty; index < values.Length; index++)
        {
            work.Charge();
            if (!float.IsFinite(vector.Values[index]))
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
            values[index] = vector.Values[index];
        }
        records.Add(vector.Id, new(new string(vector.Id.AsSpan()), initial.Scope.Field, initial.Scope.Space,
            ImmutableCollectionsMarshal.AsImmutableArray(values), receipt.Revision));
    }

    internal NativeAnnPendingReplay Pending(NativeAnnManifest admittedUpper, long after,
        CommitProjectionBatchRequest intent)
    {
        work.Check();
        if (admittedUpper.Consumer != owner.Consumer || admittedUpper.IndexGeneration != owner.IndexGeneration
            || intent.Consumer != owner.Consumer || !intent.Effects.IsEmpty || intent.CommandId == Guid.Empty
            || after >= through || through > admittedUpper.Source.ThroughSequence)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        Reserve(checked(retained + AnnSeedAccounting.ArrayAllowance(ReferenceBytes, records.Count)
            + options.HashScratchBytes));
        var sorted = records.Values.ToArray();
        AnnSeedSort.Sort(sorted, sorted.Length, work);
        var rows = ImmutableCollectionsMarshal.AsImmutableArray(sorted);
        var digest = AnnSeedFingerprint.Compute(initial.Scope, rows, work, new byte[options.HashScratchBytes]);
        return new(NativeAnnProtocol.Version, admittedUpper, after, through, rows, digest, intent);
    }

    internal AnnSeed Finish(AnnSeed upper)
    {
        work.Check();
        if (initial.DependencySha256 is null || initial.DependencySha256 != upper.DependencySha256)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
        if (through != upper.Cut.OutboxTail || initial.Scope.PrincipalId != upper.Scope.PrincipalId
            || initial.Scope.PolicyEpoch != upper.Scope.PolicyEpoch || initial.Scope.SchemaVersion != upper.Scope.SchemaVersion)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource); }
        var visible = new HashSet<string>(StringComparer.Ordinal);
        Reserve(checked(retained + AnnSeedAccounting.ArrayAllowance(DictionaryEntryBytes, upper.Records.Length)
            + AnnSeedAccounting.ArrayAllowance(ReferenceBytes, records.Count)));
        foreach (var record in upper.Records)
        { work.Charge(); visible.Add(record.DocumentId); }
        foreach (var id in records.Keys.ToArray())
        { work.Charge(); if (!visible.Contains(id)) { records.Remove(id); } }
        return VerifyUpper(upper);
    }

    private AnnSeed VerifyUpper(AnnSeed upper)
    {
        if (records.Count != upper.Records.Length)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
        Reserve(checked(retained + AnnSeedAccounting.ArrayAllowance(ReferenceBytes, records.Count) + options.HashScratchBytes));
        var sorted = records.Values.ToArray();
        AnnSeedSort.Sort(sorted, sorted.Length, work);
        var immutable = ImmutableCollectionsMarshal.AsImmutableArray(sorted);
        var digest = AnnSeedFingerprint.Compute(upper.Scope, immutable, work, new byte[options.HashScratchBytes]);
        if (digest != upper.CorpusSha256)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
        work.Check();
        return new(upper.Scope, upper.Cut, immutable, digest, retained, peak, Empty, work.Units)
        { DependencySha256 = upper.DependencySha256, ProjectionCheckpoint = upper.ProjectionCheckpoint };
    }

    private void Reserve(long bytes)
    {
        if (retained > options.MaxOwnedBytes || bytes > options.MaxPeakBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.ByteLimit); }
        peak = Math.Max(peak, bytes);
        work.Check();
    }
}
