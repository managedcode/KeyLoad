using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class BoundLeaseDisposalReports
{
    internal static Task Begin(RequestWorkLease parent, Exception? original, Exception? cleanup,
        bool completionFirst)
    {
        if (completionFirst)
        {
            _ = parent.CompleteAsync(original, cleanup);
        }
        else
        {
            parent.PrepareDisposal(original, cleanup);
        }
        return parent.DisposeAsync().AsTask();
    }

    internal static async Task ChargedAsync(RequestWorkOwner owner, Guid requestId)
    {
        RequestWorkLease? unexpected = null;
        try
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                unexpected = owner.Acquire(requestId, RequestWorkKind.RequestProducer));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        finally
        {
            if (unexpected is not null)
            {
                await unexpected.DisposeAsync();
            }
        }
    }

    internal static async Task CleanupAsync(RequestWorkOriginalSettlement outcome, Exception? expected)
    {
        if (expected is null)
        {
            await Assert.That(outcome.CleanupFailures.IsEmpty).IsTrue();
            return;
        }
        await Assert.That(outcome.CleanupFailures.Length).IsEqualTo(1);
        await Assert.That(outcome.CleanupFailures.Single()).IsSameReferenceAs(expected);
    }
}
