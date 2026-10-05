using KeyLoad.Core;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace KeyLoad.Orleans;

internal sealed class RuntimeJournalStorageState
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly RuntimeJournalClient client;
    private readonly IOptions<GrainRoutingOptions> routingOptions;
    private bool retired;

    internal RuntimeJournalStorageState(RuntimeJournalClient client, JournalId journalId,
        RuntimeJournalOptions options, string format, IOptions<GrainRoutingOptions> routingOptions)
    {
        this.client = client;
        JournalId = journalId;
        Options = options;
        Format = format;
        this.routingOptions = routingOptions;
    }

    internal JournalId JournalId { get; }
    internal RuntimeJournalOptions Options { get; }
    internal string Format { get; }
    internal RuntimeJournalSnapshot? Captured { get; private set; }

    internal async ValueTask<Lease> EnterAsync(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(routingOptions.Value.ExecutionLifetime);
        try
        {
            await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            return new(gate, deadline);
        }
        catch
        {
            deadline.Dispose();
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

    internal sealed class Lease(SemaphoreSlim gate, CancellationTokenSource deadline) : IDisposable
    {
        internal CancellationToken Token => deadline.Token;

        public void Dispose()
        {
            gate.Release();
            deadline.Dispose();
        }
    }
}
