using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace KeyLoad.Core.Features.Search;

internal sealed class AnnSeedBuffer
{
    private const int EmptyRecordCapacity = 0;
    private const int RecordArrayGrowthFactor = 2;
    private const int ReferenceSlotBytes = 8;
    private const int FirstRecordIndex = 0;

    private const string CorruptSource = "The canonical ANN seed source is inconsistent.";
    private readonly int maximumRecords;
    private readonly long maximumOwnedBytes;
    private readonly long maximumPeakBytes;
    private readonly int initialRecordCapacity;
    private readonly ReadExecutionBudget budget;
    private readonly AnnSeedWork work;
    private VectorRecord[] records;
    private int count;
    private long owned;
    private long maximumOwned;
    private long peak;
    private AnnSeedScope? scope;
    private AnnSeedCut cut;
    private bool hasCut;

    internal AnnSeedBuffer(int maximumRecords, long maximumOwnedBytes, long maximumPeakBytes, int initialRecordCapacity, int hashScratchBytes,
        ReadExecutionBudget budget, AnnSeedWork work)
    {
        const int LengthEmptyCount = 0;

        this.maximumRecords = maximumRecords;
        this.maximumOwnedBytes = maximumOwnedBytes;
        this.maximumPeakBytes = maximumPeakBytes;
        this.initialRecordCapacity = initialRecordCapacity;
        this.budget = budget;
        this.work = work;
        owned = checked(AnnSeedAccounting.FixedBytes(hashScratchBytes) + AnnSeedAccounting.ArrayAllowance(ReferenceSlotBytes, LengthEmptyCount));
        peak = owned;
        Admit(owned, owned);
        HashScratch = new byte[hashScratchBytes];
        records = Array.Empty<VectorRecord>();
    }

    internal byte[] HashScratch { get; }
    internal string? DependencySha256 { get; set; }
    internal long? ProjectionCheckpoint { get; set; }
    private AnnSeedScope Scope => scope ?? throw Errors.Fail(ErrorCode.Corruption, CorruptSource);

    internal void InitializeScope(PrincipalRecord principal, PartitionRef partition, string collection,
        string field, VectorSpace space, long schemaVersion, DateTimeOffset evaluatedAt)
    {
        work.Check();
        scope = AnnSeedScopeCapture.Capture(this, principal, partition, collection, field, space, schemaVersion, evaluatedAt);
    }

    internal void SetCut(AnnSeedCut value)
    {
        cut = value;
        hasCut = true;
    }

    internal void Observe(DocumentRecord document, VectorRecord vector, PartitionRef partition,
        string collection, string field, VectorSpace requestedSpace)
    {
        const long VectorRowObjectAllowanceBytes = 64L;
        const int SingleValueBytes = 4;

        work.Check();
        AnnSeedSourceValidator.Validate(vector, document, partition, collection, field, work);
        if (!AnnSeedSourceValidator.SpaceMatches(vector.Space, requestedSpace, work))
        {
            return;
        }
        if (count >= maximumRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.RecordLimit);
        }
        var rowBytes = checked(VectorRowObjectAllowanceBytes + AnnSeedAccounting.ArrayAllowance(SingleValueBytes, vector.Values.Length));
        var idAllowance = AnnSeedAccounting.StringAllowance(vector.DocumentId);
        PreflightRow(rowBytes, idAllowance);
        EnsureCapacity();
        ReserveOwned(rowBytes);
        var id = CopyString(vector.DocumentId);
        var values = new float[vector.Values.Length];
        for (var index = FirstRecordIndex; index < values.Length; index++)
        {
            work.Charge();
            var value = vector.Values[index];
            if (!float.IsFinite(value))
            {
                throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
            }
            values[index] = value;
        }
        records[count++] = new(id, Scope.Field, Scope.Space,
            ImmutableCollectionsMarshal.AsImmutableArray(values), vector.DocumentRevision);
    }

    internal AnnSeedCaptured Finish()
    {
        if (!hasCut || scope is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSource);
        }
        var immutableRecords = FinishRecords();
        work.Check();
        return new(scope, cut, immutableRecords, HashScratch, maximumOwned, peak, work)
        { DependencySha256 = DependencySha256, ProjectionCheckpoint = ProjectionCheckpoint };
    }

    internal ImmutableArray<VectorRecord> FinishRecords()
    {
        var exact = Trim();
        AnnSeedSort.Sort(exact, count, work);
        return ImmutableCollectionsMarshal.AsImmutableArray(exact);
    }

    private VectorRecord[] Trim()
    {

        if (records.Length == count)
        {
            return records;
        }
        var finalAllowance = AnnSeedAccounting.ArrayAllowance(ReferenceSlotBytes, count);
        var currentAllowance = AnnSeedAccounting.ArrayAllowance(ReferenceSlotBytes, records.Length);
        var finalOwned = checked(owned - currentAllowance + finalAllowance);
        Admit(finalOwned, checked(owned + finalAllowance));
        var exact = new VectorRecord[count];
        for (var index = FirstRecordIndex; index < count; index++)
        {
            work.Charge();
            exact[index] = records[index];
        }
        owned = checked(owned - currentAllowance + finalAllowance);
        records = exact;
        return exact;
    }

    internal string CopyString(string value)
    {
        work.Charge(value.Length);
        var bytes = AnnSeedValidation.Utf8Length(value);
        ReserveOwned(AnnSeedAccounting.StringAllowance(value));
        work.Charge(bytes);
        return new(value.AsSpan());
    }

    private void PreflightRow(long rowBytes, long idAllowance)
    {

        var nextOwned = owned;
        var growthPeak = owned;
        if (count == records.Length)
        {
            var next = NextCapacity();
            var nextAllowance = AnnSeedAccounting.ArrayAllowance(ReferenceSlotBytes, next);
            var oldAllowance = AnnSeedAccounting.ArrayAllowance(ReferenceSlotBytes, records.Length);
            nextOwned = checked(owned - oldAllowance + nextAllowance);
            growthPeak = checked(owned + nextAllowance);
        }
        var finalOwned = checked(nextOwned + rowBytes + idAllowance);
        Admit(finalOwned, Math.Max(growthPeak, finalOwned));
    }

    private int NextCapacity()
        => records.Length == EmptyRecordCapacity ? Math.Min(initialRecordCapacity, maximumRecords)
            : Math.Min(maximumRecords, checked(records.Length * RecordArrayGrowthFactor));

    private void EnsureCapacity()
    {
        if (count < records.Length)
        {
            return;
        }
        var next = NextCapacity();
        var nextAllowance = AnnSeedAccounting.ArrayAllowance(ReferenceSlotBytes, next);
        var oldAllowance = AnnSeedAccounting.ArrayAllowance(ReferenceSlotBytes, records.Length);
        var nextOwned = checked(owned - oldAllowance + nextAllowance);
        var nextPeak = checked(owned + nextAllowance);
        Admit(nextOwned, nextPeak);
        var expanded = new VectorRecord[next];
        for (var index = FirstRecordIndex; index < count; index++)
        {
            work.Charge();
            expanded[index] = records[index];
        }
        owned = checked(owned - oldAllowance + nextAllowance);
        records = expanded;
    }

    private void ReserveOwned(long amount)
    {
        var next = checked(owned + amount);
        if (next > maximumOwnedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.ByteLimit);
        }
        Admit(next, next);
        owned = next;
    }

    private void Admit(long ownedCandidate, long peakCandidate)
    {
        budget.Check();
        if (ownedCandidate > maximumOwnedBytes || peakCandidate > maximumPeakBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.ByteLimit);
        }
        maximumOwned = Math.Max(maximumOwned, ownedCandidate);
        peak = Math.Max(peak, peakCandidate);
    }

}
