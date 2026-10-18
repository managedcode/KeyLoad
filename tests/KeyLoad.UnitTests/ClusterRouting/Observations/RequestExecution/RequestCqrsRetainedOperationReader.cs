
namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Reads the complete original bounded journal cut under the original native protocol owner.</summary>
internal static class RequestCqrsRetainedOperationReader
{
    private const long FirstRetainedEntry = 1;
    private const long NextRetainedEntry = 1;

    internal static ReplicatedOperation Read(DurableReplicaLog log, Guid commandId, DatabaseLimits limits,
        CancellationToken token)
    {
        log.ProtocolGate.Wait(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var last = log.State.LastIndex;
            if (last < FirstRetainedEntry || last > limits.MaxScanRecords)
            {
                throw new InvalidOperationException();
            }
            var entries = new List<ReplicaEntry>();
            for (var index = FirstRetainedEntry; index <= last; index += NextRetainedEntry)
            {
                token.ThrowIfCancellationRequested();
                var entry = log.ReadEntry(index) ?? throw new InvalidOperationException();
                entries.Add(entry);
                if (ReplicaProtocolCodec.MeasureEntries(entries) > limits.MaxBatchBytes)
                {
                    throw new InvalidOperationException();
                }
                token.ThrowIfCancellationRequested();
            }
            token.ThrowIfCancellationRequested();
            return entries.SingleOrDefault(entry => entry.Operation?.Id == commandId)?.Operation
                ?? throw new InvalidOperationException();
        }
        finally
        {
            log.ProtocolGate.Release();
        }
    }
}
