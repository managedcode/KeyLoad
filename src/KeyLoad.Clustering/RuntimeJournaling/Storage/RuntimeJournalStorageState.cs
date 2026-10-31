#pragma warning disable ORLEANSEXP005
using KeyLoad.Core;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace KeyLoad.Clustering;

internal sealed class RuntimeJournalStorageState(RuntimeJournalClient client, JournalId journalId,
    IOptions<RuntimeJournalOptions> options, string format, SemaphoreSlim[] gates, int gateIndex)
{
    private bool retired;

    internal JournalId JournalId { get; } = journalId;
    internal IOptions<RuntimeJournalOptions> Options { get; } = options;
    internal string Format { get; } = format;
    internal RuntimeJournalSnapshot? Captured
    {
        get; private set;
    }

    internal async ValueTask<Lease> EnterAsync(CancellationToken cancellationToken)
    {
        var lease = new Lease(gates[gateIndex], cancellationToken);
        try
        {
            await lease.AcquireAsync();
            return lease;
        }
        catch (Exception)
        {
            lease.Dispose();
            throw;
        }
    }

    internal async Task<(bool HasValue, RuntimeJournalSnapshot? Value)> CaptureAsync(CancellationToken cancellationToken)
    {
        if (retired)
        {
            return (false, default(RuntimeJournalSnapshot?));
        }

        if (Captured is null)
        {
            Captured = await client.GetHeaderAsync(JournalId.Value, cancellationToken);
        }

        return ((RuntimeJournalSnapshot?)(Captured)) is { } outcomeValue2 ? (true, outcomeValue2) : (false, default(RuntimeJournalSnapshot?));
    }

    internal async Task<(bool HasValue, RuntimeJournalSnapshot? Value)> ObserveAsync(CancellationToken cancellationToken)
    {
        if (retired)
        {
            return (false, default(RuntimeJournalSnapshot?));
        }

        var observed = await client.GetHeaderAsync(JournalId.Value, cancellationToken);
        CaptureInitial(observed);
        return ((RuntimeJournalSnapshot?)(observed)) is { } outcomeValue2 ? (true, outcomeValue2) : (false, default(RuntimeJournalSnapshot?));
    }

    internal void CaptureInitial(RuntimeJournalSnapshot? snapshot)
    {
        if (!retired && Captured is null && snapshot is not null)
        {
            Captured = snapshot;
        }
    }

    internal void AdvanceAfterBodyWrite(RuntimeJournalSnapshot expected, RuntimeJournalSnapshot? acknowledged)
    {
        if (acknowledged is not null && IsCurrent(expected)
            && acknowledged.InstanceId == expected.InstanceId
            && acknowledged.OwnerGeneration == expected.OwnerGeneration
            && acknowledged.ContentRevision > expected.ContentRevision)
        {
            Captured = acknowledged;
        }
    }

    internal void AdvanceAfterMetadataWrite(RuntimeJournalSnapshot? expected, RuntimeJournalSnapshot? acknowledged)
    {
        if (acknowledged is null)
        {
            return;
        }

        if (expected is null)
        {
            CaptureInitial(acknowledged);
        }
        else if (IsCurrent(expected) && acknowledged.InstanceId == expected.InstanceId
            && acknowledged.OwnerGeneration == expected.OwnerGeneration
            && acknowledged.ContentRevision == expected.ContentRevision)
        {
            Captured = acknowledged;
        }
    }

    internal bool IsCurrent(RuntimeJournalSnapshot expected)
        => !retired && Captured is { } current && current.InstanceId == expected.InstanceId
            && current.OwnerGeneration == expected.OwnerGeneration
            && current.ContentRevision == expected.ContentRevision;

    internal bool IsRetired => retired;

    internal void Retire() => retired = true;

    internal sealed class Lease(SemaphoreSlim gate, CancellationToken callerToken) : IDisposable
    {
        private bool acquired;

        internal CancellationToken Token => callerToken;

        internal async ValueTask AcquireAsync()
        {
            await gate.WaitAsync(callerToken);
            acquired = true;
        }

        public void Dispose()
        {
            if (acquired)
            {
                acquired = false;
                gate.Release();
            }
        }
    }
}
#pragma warning restore ORLEANSEXP005
