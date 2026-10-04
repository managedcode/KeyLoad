using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace KeyLoad.Core.Features.Search;

internal sealed class AnnSeedBuffer
{
    private const string CorruptSource = "The canonical ANN seed source is inconsistent.";
    private readonly AnnSeedOptions options;
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

    internal AnnSeedBuffer(AnnSeedOptions options, ReadExecutionBudget budget, AnnSeedWork work)
    {
        this.options = options;
        this.budget = budget;
        this.work = work;
        owned = checked(AnnSeedAccounting.FixedBytes + AnnSeedAccounting.ArrayAllowance(8, 0));
        peak = owned;
        Admit(owned, owned);
        HashScratch = new byte[AnnSeedAccounting.HashScratchBytes];
        records = Array.Empty<VectorRecord>();
    }

    internal byte[] HashScratch { get; }
    private AnnSeedScope Scope => scope ?? throw Errors.Fail(ErrorCode.Corruption, CorruptSource);

    internal void InitializeScope(PrincipalRecord principal, PartitionRef partition, string collection,
        string field, VectorSpace space, long schemaVersion, DateTimeOffset evaluatedAt)
    {
        work.Check();
        var principalId = CopyString(principal.Id);
        var tenantId = CopyString(partition.TenantId);
        var databaseId = CopyString(partition.DatabaseId);
        var domainId = CopyString(partition.TransactionDomainId);
        var partitionKey = CopyString(partition.PartitionKey);
        var ownedPartition = new PartitionRef(tenantId, databaseId, domainId, partitionKey);
        var ownedCollection = CopyString(collection);
        var ownedField = CopyString(field);
        var ownedSpace = new VectorSpace(CopyString(space.Id), space.Dimension, space.Metric,
            CopyString(space.Model), CopyString(space.Version));
        scope = new(principalId, principal.PolicyEpoch, ownedPartition, ownedCollection,
            ownedField, schemaVersion, ownedSpace, evaluatedAt);
    }

    internal void SetCut(AnnSeedCut value)
    {
        cut = value;
        hasCut = true;
    }

    internal void Observe(DocumentRecord document, VectorRecord vector, PartitionRef partition,
        string collection, string field, VectorSpace requestedSpace)
    {
        work.Check();
        AnnSeedSourceValidator.Validate(vector, document, partition, collection, field, work);
        if (!AnnSeedSourceValidator.SpaceMatches(vector.Space, requestedSpace, work))
        {
            return;
        }
        if (count >= options.MaxRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.RecordLimit);
        }
        var rowBytes = checked(64L + AnnSeedAccounting.ArrayAllowance(4, vector.Values.Length));
        var idAllowance = AnnSeedAccounting.StringAllowance(vector.DocumentId);
        PreflightRow(rowBytes, idAllowance);
        EnsureCapacity();
        ReserveOwned(rowBytes);
        var id = CopyString(vector.DocumentId);
        var values = new float[vector.Values.Length];
        for (var index = 0; index < values.Length; index++)
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
        return new(scope, cut, immutableRecords, HashScratch, maximumOwned, peak, work);
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
        var finalAllowance = AnnSeedAccounting.ArrayAllowance(8, count);
        var currentAllowance = AnnSeedAccounting.ArrayAllowance(8, records.Length);
        var finalOwned = checked(owned - currentAllowance + finalAllowance);
        Admit(finalOwned, checked(owned + finalAllowance));
        var exact = new VectorRecord[count];
        for (var index = 0; index < count; index++)
        {
            work.Charge();
            exact[index] = records[index];
        }
        owned = checked(owned - currentAllowance + finalAllowance);
        records = exact;
        return exact;
    }

    private string CopyString(string value)
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
            var nextAllowance = AnnSeedAccounting.ArrayAllowance(8, next);
            var oldAllowance = AnnSeedAccounting.ArrayAllowance(8, records.Length);
            nextOwned = checked(owned - oldAllowance + nextAllowance);
            growthPeak = checked(owned + nextAllowance);
        }
        var finalOwned = checked(nextOwned + rowBytes + idAllowance);
        Admit(finalOwned, Math.Max(growthPeak, finalOwned));
    }

    private int NextCapacity()
        => records.Length == 0 ? Math.Min(32, options.MaxRecords)
            : Math.Min(options.MaxRecords, checked(records.Length * 2));

    private void EnsureCapacity()
    {
        if (count < records.Length)
        {
            return;
        }
        var next = NextCapacity();
        var nextAllowance = AnnSeedAccounting.ArrayAllowance(8, next);
        var oldAllowance = AnnSeedAccounting.ArrayAllowance(8, records.Length);
        var nextOwned = checked(owned - oldAllowance + nextAllowance);
        var nextPeak = checked(owned + nextAllowance);
        Admit(nextOwned, nextPeak);
        var expanded = new VectorRecord[next];
        for (var index = 0; index < count; index++)
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
        if (next > options.MaxOwnedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.ByteLimit);
        }
        Admit(next, next);
        owned = next;
    }

    private void Admit(long ownedCandidate, long peakCandidate)
    {
        budget.Check();
        if (ownedCandidate > options.MaxOwnedBytes || peakCandidate > options.MaxPeakBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.ByteLimit);
        }
        maximumOwned = Math.Max(maximumOwned, ownedCandidate);
        peak = Math.Max(peak, peakCandidate);
    }

}
