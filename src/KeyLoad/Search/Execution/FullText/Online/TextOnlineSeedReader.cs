using KeyLoad.Core;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Core.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Search;

internal sealed class TextOnlineSeedReader(ZoneTreeReadCutLease execution,
    ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant, TextSeedPin scope)
{
    private const int AllocationOnlyRecords = 0;
    private const long UnissuedDocumentRevision = 0;
    private const int EmptyDocumentCapacity = 0;
    private const int FirstDocumentCapacity = 1;
    private const int DocumentCapacityGrowthFactor = 2;
    private const string InvalidSeed = "The online text seed does not match its captured canonical source.";
    private const string InvalidAccounting = "The online text seed execution accounting is inconsistent.";
    private readonly List<DocumentRecord> documents = [];
    private readonly int maximumRecords = grant.RemainingRecords;
    private long runtimeAdmittedBytes;
    private int runtimeAdmittedRecords;

    internal DocumentRecord[] Read()
    {
        var failures = new List<Exception>();
        var completed = false;
        ServerFailureObserver.Observe(() =>
        {
            execution.VisitPrefix(DocumentStorageKeys.Prefix(scope.Consumer.Partition, scope.Collection),
                Retain, RuntimeAdmit);
            completed = true;
        }, failures);
        ServerFailureObserver.Observe(() => ValidateSettledAccounting(completed), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        budget.ImportReadGrant(grant, checked((long)documents.Count * IntPtr.Size), AllocationOnlyRecords);
        return documents.ToArray();
    }

    private void RuntimeAdmit(long bytes, int records)
    {
        budget.ImportReadGrant(grant, bytes, records);
        runtimeAdmittedBytes = checked(runtimeAdmittedBytes + bytes);
        runtimeAdmittedRecords = checked(runtimeAdmittedRecords + records);
    }

    private bool Retain(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        budget.ImportReadGrant(grant, value.Length, AllocationOnlyRecords);
        ReserveArray();
        var document = OrleansSerialization.Deserialize<DocumentRecord>(value);
        if (document.Reference.Partition != scope.Consumer.Partition
            || document.Reference.Collection != scope.Collection || document.Revision <= UnissuedDocumentRevision
            || !key.SequenceEqual(DocumentStorageKeys.RecordKey(document.Reference)))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidSeed);
        }
        documents.Add(document);
        return true;
    }

    private void ReserveArray()
    {
        if (documents.Count < documents.Capacity)
        {
            return;
        }
        var capacity = documents.Capacity == EmptyDocumentCapacity ? FirstDocumentCapacity
            : (int)Math.Min(maximumRecords, checked((long)documents.Capacity * DocumentCapacityGrowthFactor));
        if (capacity <= documents.Count)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidAccounting);
        }
        budget.ImportReadGrant(grant, checked((long)capacity * IntPtr.Size), AllocationOnlyRecords);
        documents.Capacity = capacity;
    }

    private void ValidateSettledAccounting(bool completed)
    {
        var observed = execution.ObserveSettledWork();
        if (observed.ExaminedBytes < runtimeAdmittedBytes || observed.Records < runtimeAdmittedRecords
            || (completed && (observed.ExaminedBytes != runtimeAdmittedBytes
                || observed.Records != runtimeAdmittedRecords)))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAccounting);
        }
    }
}
