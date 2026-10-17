using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorCurrentSourceRound : IDisposable
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;
    private readonly EventVectorCapturedExchange[] captures;
    private EventVectorFirstWitnessOwner? witness;

    private EventVectorCurrentSourceRound(EventVectorCapturedExchange[] captures) => this.captures = captures;
    internal ReadOnlyMemory<byte> Witness => witness?.OriginalWitnessBytes
        ?? throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);

    internal static async Task<EventVectorCurrentSourceRound> CreateAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options, PhysicalOwnerDirectoryV1 directory,
        EventFeedControlRequest request, string principalId, DateTimeOffset expiry,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        admission.RequireEntryCount(directory.Owners.Length);
        work.ChargeBytes(checked((long)directory.Owners.Length * IntPtr.Size));
        var owner = new EventVectorCurrentSourceRound(new EventVectorCapturedExchange[directory.Owners.Length]);
        var completed = false;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                for (var ordinal = FirstOrdinal; ordinal < owner.captures.Length; ordinal++)
                {
                    owner.captures[ordinal] = await EventVectorParentCapture.ExecuteAsync(runtime, node, partition,
                        options, directory.Owners[ordinal], new(Version, request, principalId, ordinal), expiry,
                        admission, encoding, work, cancellationToken).ConfigureAwait(false);
                }
                var entries = EventVectorFirstCaptureSet.Read(directory, request, principalId, owner.captures, admission, work);
                owner.witness = EventVectorFirstWitnessOwner.Create(entries, owner.captures, admission, encoding, work);
                completed = true;
            }, failures).ConfigureAwait(false);
        }
        finally { if (!completed) { ServerFailureObserver.Observe(owner.Dispose, failures); } }
        ServerFailureObserver.ThrowIfAny(failures);
        return owner;
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        if (witness is not null)
        {
            ServerFailureObserver.Observe(witness.Dispose, failures);
        }
        foreach (var capture in captures)
        {
            if (capture is not null)
            {
                ServerFailureObserver.Observe(capture.Dispose, failures);
            }
        }
        witness = null;
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
