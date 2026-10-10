using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineSeedReader(ZoneTreeReadCutLease native,
    ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant, NativeTextSeedPin scope)
{
    private const int AllocationOnlyRecords = 0;
    private const long UnissuedDocumentRevision = 0;
    private const int EmptyDocumentCapacity = 0;
    private const int FirstDocumentCapacity = 1;
    private const int DocumentCapacityGrowthFactor = 2;
    private const string InvalidSeed = "The online text seed does not match its captured canonical source.";
    private const string InvalidAccounting = "The online text seed native accounting is inconsistent.";
    private readonly List<DocumentRecord> documents = [];
    private readonly int maximumRecords = grant.RemainingRecords;
    private long admittedNativeBytes;
    private int admittedNativeRecords;

    internal DocumentRecord[] Read()
    {
        var failures = new List<Exception>();
        var completed = false;
        ServerFailureObserver.Observe(() =>
        {
            native.VisitPrefix(DocumentStorageKeys.Prefix(scope.Consumer.Partition, scope.Collection),
                Retain, AdmitNative);
            completed = true;
        }, failures);
        ServerFailureObserver.Observe(() => ValidateSettledAccounting(completed), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        budget.ImportReadGrant(grant, checked((long)documents.Count * IntPtr.Size), AllocationOnlyRecords);
        return documents.ToArray();
    }

    private void AdmitNative(long bytes, int records)
    {
        budget.ImportReadGrant(grant, bytes, records);
        admittedNativeBytes = checked(admittedNativeBytes + bytes);
        admittedNativeRecords = checked(admittedNativeRecords + records);
    }

    private bool Retain(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        budget.ImportReadGrant(grant, value.Length, AllocationOnlyRecords);
        ReserveArray();
        var document = NativeSerialization.Deserialize<DocumentRecord>(value);
        if (document.Reference.Partition != scope.Consumer.Partition
            || document.Reference.Collection != scope.Collection || document.Revision <= UnissuedDocumentRevision
            || !key.SequenceEqual(DocumentStorageKeys.RecordKey(document.Reference)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidSeed); }
        documents.Add(document);
        return true;
    }

    private void ReserveArray()
    {
        if (documents.Count < documents.Capacity)
        { return; }
        var capacity = documents.Capacity == EmptyDocumentCapacity ? FirstDocumentCapacity
            : (int)Math.Min(maximumRecords, checked((long)documents.Capacity * DocumentCapacityGrowthFactor));
        if (capacity <= documents.Count)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidAccounting); }
        budget.ImportReadGrant(grant, checked((long)capacity * IntPtr.Size), AllocationOnlyRecords);
        documents.Capacity = capacity;
    }

    private void ValidateSettledAccounting(bool completed)
    {
        var observed = native.ObserveSettledWork();
        if (observed.ExaminedBytes < admittedNativeBytes || observed.Records < admittedNativeRecords
            || (completed && (observed.ExaminedBytes != admittedNativeBytes
                || observed.Records != admittedNativeRecords)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidAccounting); }
    }
}
