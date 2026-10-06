#pragma warning disable ORLEANSEXP005
using KeyLoad.Core;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace KeyLoad.Orleans;

internal sealed class RuntimeJournalStorageState(RuntimeJournalClient client, JournalId journalId,
    IOptions<RuntimeJournalOptions> options, string format, IOptions<GrainRoutingOptions> routingOptions,
    SemaphoreSlim[] gates, int gateIndex)
{
    private bool retired;

    internal JournalId JournalId { get; } = journalId;
    internal IOptions<RuntimeJournalOptions> Options { get; } = options;
    internal string Format { get; } = format;
    internal RuntimeJournalSnapshot? Captured { get; private set; }

    internal async ValueTask<Lease> EnterAsync(CancellationToken cancellationToken)
    {
        var lease = new Lease(gates[gateIndex], routingOptions.Value.ExecutionLifetime, cancellationToken);
        try
        {
            await lease.AcquireAsync().ConfigureAwait(false);
            return lease;
        }
        catch (Exception)
        {
            lease.Dispose();
            throw;
        }
    }

    internal async Task<RuntimeJournalSnapshot?> CaptureAsync(CancellationToken cancellationToken)
    {
        if (retired)
        {
            return null;
        }

        if (Captured is null)
        {
            Captured = await client.GetHeaderAsync(JournalId.Value, cancellationToken).ConfigureAwait(false);
        }

        return Captured;
    }

    internal async Task<RuntimeJournalSnapshot?> ObserveAsync(CancellationToken cancellationToken)
    {
        if (retired)
        {
            return null;
        }

        var observed = await client.GetHeaderAsync(JournalId.Value, cancellationToken).ConfigureAwait(false);
        CaptureInitial(observed);
        return observed;
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

    internal sealed class Lease(SemaphoreSlim gate, TimeSpan timeout, CancellationToken callerToken) : IDisposable
    {
        private readonly CancellationTokenSource deadline = CreateDeadline(timeout, callerToken);
        private bool acquired;

        internal CancellationToken Token => deadline.Token;

        internal async ValueTask AcquireAsync()
        {
            await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
        }

        public void Dispose()
        {
            try
            {
                if (acquired)
                {
                    acquired = false;
                    gate.Release();
                }
            }
            finally
            {
                deadline.Dispose();
            }
        }

        private static CancellationTokenSource CreateDeadline(TimeSpan timeout, CancellationToken callerToken)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            source.CancelAfter(timeout);
            return source;
        }
    }
}
#pragma warning restore ORLEANSEXP005
