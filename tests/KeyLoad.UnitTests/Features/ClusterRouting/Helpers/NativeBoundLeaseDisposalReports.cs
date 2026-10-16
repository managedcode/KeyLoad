using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalReports
{
    internal static Task Begin(NativeRequestWorkLease parent, Exception? original, Exception? cleanup,
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

    internal static async Task ChargedAsync(NativeRequestWorkOwner owner, Guid requestId)
    {
        NativeRequestWorkLease? unexpected = null;
        try
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                unexpected = owner.Acquire(requestId, NativeRequestWorkKind.RequestProducer));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        finally
        {
            unexpected?.Dispose();
        }
    }

    internal static async Task CleanupAsync(NativeRequestWorkOriginalSettlement outcome, Exception? expected)
    {
        if (expected is null)
        {
            await Assert.That(outcome.CleanupFailures.IsEmpty).IsTrue();
            return;
        }
        await Assert.That(outcome.CleanupFailures.Count()).IsEqualTo(1);
        await Assert.That(outcome.CleanupFailures.Single()).IsSameReferenceAs(expected);
    }
}
