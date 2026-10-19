using System.Runtime.InteropServices;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnReplay
{
    private const int Empty = 0;
    private const int DictionaryEntryBytes = 128;
    private const int ReferenceBytes = 8;
    private readonly AnnReplaySource initial;
    private readonly AnnOwnedKey owner;
    private readonly bool applyDeltas;
    private readonly AnnSeedOptions options;
    private readonly AnnSeedWork work;
    private readonly Dictionary<string, VectorRecord> records = new(StringComparer.Ordinal);
    private readonly Dictionary<long, (long Through, byte[] Digest)> appliedPages = [];
    private long retained;
    private long peak;
    private long through;

    internal AnnReplay(AnnReplaySource seed, AnnOwnedKey owner, IOptions<AnnSeedOptions> configured, ReadExecutionBudget budget, bool applyDeltas = true)
    {
        var options = configured.Value;
        options.Validate();
        initial = seed;
        this.owner = owner;
        this.applyDeltas = applyDeltas;
        this.options = options;
        work = new(budget, options.MaxWorkUnits);
        through = seed.ThroughSequence;
        retained = checked(seed.OwnedBytesUpperBound + AnnSeedAccounting.ArrayAllowance(DictionaryEntryBytes, seed.Records.Length));
        Reserve(retained);
        records.EnsureCapacity(seed.Records.Length);
        foreach (var record in seed.Records)
        {
            work.Charge();
            records.Add(record.DocumentId, record);
        }
    }

    internal long WorkUnits => work.Units;
    internal long Through => through;
    internal long RetainedBytes => retained;
    internal long MaximumStagePeakBytes => options.MaxPeakBytes;

    internal void Apply(ProjectionBatch batch, long upper)
    {
        work.Check();
        if (batch.Consumer.Consumer != owner.Consumer
            || batch.Consumer.Definition.IndexGeneration != owner.IndexGeneration)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.Stale);
        }
        using var encoded = new AnnHashStream(work, options.MaxPeakBytes);
        OrleansSerialization.Serialize(new AnnReplayPage(batch.Consumer, batch.ThroughSequence, batch.Entries), encoded);
        var digest = encoded.Finish();
        if (appliedPages.TryGetValue(batch.Consumer.Checkpoint, out var applied))
        {
            if (applied.Through != batch.ThroughSequence)
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
            }
            AnnDigest.Require(applied.Digest, digest);
            return;
        }
        if (batch.Consumer.Released || batch.Consumer.Checkpoint != through
            || batch.ThroughSequence < through || batch.ThroughSequence > upper)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.Stale);
        }
        var previous = through;
        foreach (var entry in batch.Entries)
        {
            work.Charge();
            if (entry.Sequence <= previous || entry.Sequence > batch.ThroughSequence
                || entry.Commit.AtomicPartitionId != initial.Scope.Partition.AtomicPartitionId)
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
            }
            if (applyDeltas)
            {
                ApplyEntry(entry);
            }
            previous = entry.Sequence;
        }
        retained = checked(retained + DictionaryEntryBytes + AnnDigest.Bytes);
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
        => AnnReplayRecords.ApplyVector(vector, receipt, initial, options, work, records,
            ref retained, ref peak, DictionaryEntryBytes);

    internal AnnPendingReplay Pending(AnnManifest admittedUpper, long after,
        CommitProjectionBatchRequest intent)
    {
        work.Check();
        if (admittedUpper.Consumer != owner.Consumer || admittedUpper.IndexGeneration != owner.IndexGeneration
            || intent.Consumer != owner.Consumer || !intent.Effects.IsEmpty || intent.CommandId == Guid.Empty
            || after >= through || through > admittedUpper.Source.ThroughSequence)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        Reserve(checked(retained + AnnSeedAccounting.ArrayAllowance(ReferenceBytes, records.Count)
            + options.HashScratchBytes));
        var sorted = records.Values.ToArray();
        AnnSeedSort.Sort(sorted, sorted.Length, work);
        var rows = ImmutableCollectionsMarshal.AsImmutableArray(sorted);
        var digest = AnnSeedFingerprint.Compute(initial.Scope, rows, work, new byte[options.HashScratchBytes]);
        return new(AnnProtocol.Version, admittedUpper, after, through, rows, digest, intent);
    }

    internal AnnSeed Finish(AnnSeed upper)
    {
        work.Check();
        if (initial.DependencySha256 is null || initial.DependencySha256 != upper.DependencySha256)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
        if (through != upper.Cut.OutboxTail || initial.Scope.PrincipalId != upper.Scope.PrincipalId
            || initial.Scope.PolicyEpoch != upper.Scope.PolicyEpoch || initial.Scope.SchemaVersion != upper.Scope.SchemaVersion)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
        }
        var visible = new HashSet<string>(StringComparer.Ordinal);
        Reserve(checked(retained + AnnSeedAccounting.ArrayAllowance(DictionaryEntryBytes, upper.Records.Length)
            + AnnSeedAccounting.ArrayAllowance(ReferenceBytes, records.Count)));
        foreach (var record in upper.Records)
        {
            work.Charge();
            visible.Add(record.DocumentId);
        }
        foreach (var id in records.Keys.ToArray())
        {
            work.Charge();
            if (!visible.Contains(id))
            {
                records.Remove(id);
            }
        }
        return VerifyUpper(upper);
    }

    private AnnSeed VerifyUpper(AnnSeed upper)
    {
        if (records.Count != upper.Records.Length)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
        Reserve(checked(retained + AnnSeedAccounting.ArrayAllowance(ReferenceBytes, records.Count) + options.HashScratchBytes));
        var sorted = records.Values.ToArray();
        AnnSeedSort.Sort(sorted, sorted.Length, work);
        var immutable = ImmutableCollectionsMarshal.AsImmutableArray(sorted);
        var digest = AnnSeedFingerprint.Compute(upper.Scope, immutable, work, new byte[options.HashScratchBytes]);
        if (digest != upper.CorpusSha256)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
        work.Check();
        return new(upper.Scope, upper.Cut, immutable, digest, retained, peak, Empty, work.Units)
        {
            DependencySha256 = upper.DependencySha256,
            ProjectionCheckpoint = upper.ProjectionCheckpoint
        };
    }

    private void Reserve(long bytes)
        => AnnReplayRecords.Reserve(bytes, ref retained, ref peak, options, work);
}
