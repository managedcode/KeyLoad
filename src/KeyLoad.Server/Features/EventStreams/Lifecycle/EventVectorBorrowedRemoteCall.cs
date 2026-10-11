using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorBorrowedRemoteCall
{
    internal static async Task<EventVectorAuthenticatedExchange> ExecuteAsync(RemoteDocumentWorkOwner originalOwner,
        RemoteDocumentClient? originalClient, RegisteredPhysicalOwnerV1 destination,
        EventVectorRemoteCall originalCall, EventVectorRemoteEncoding encoding, ReadExecutionBudget originalWork,
        CancellationToken originalCancellationToken)
    {
        if (originalClient is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        RemoteDocumentWorkOwner.Operation? operation = null;
        EventVectorAuthenticatedExchange? result = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                operation = originalOwner.Acquire(originalCall.RequestId);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    originalCancellationToken, operation.ShutdownToken);
                result = await originalClient.SendEventVectorAsync(destination, originalCall,
                    encoding, originalWork, linked.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (operation is not null)
            { ServerFailureObserver.Observe(operation.Dispose, failures); }
        }
        if (failures.Count != EventVectorLocalCallProtocol.NoFailures)
        {
            if (result is not null)
            { ServerFailureObserver.Observe(result.Dispose, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return result ?? throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
    }
}
