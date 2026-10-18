namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalOperation
{
    internal static async Task ExecuteAsync(NativeBoundLeaseSeeded state,
        NativeBoundLeaseOperation operation, Exception? original, Exception? cleanup, bool completionFirst)
    {
        var input = NativeBoundLeaseQuery.Create(state, operation);
        await NativeBoundLeaseAuthorization.ReceivingTenantAsync(state, operation, input);
        await NativeBoundLeaseAssertions.QueryAsync(state, input,
            NativeBoundLeaseExecution.Read(state, operation, input));
        operation.Parent.AdmitPublication();
        await NativeBoundLeaseAssertions.RetentionAsync(operation, input.Coverage.Lifetimes.Single());
        var parent = operation.Parent;
        var synchronous = Assert.ThrowsExactly<KeyLoadException>(parent.Dispose);
        await Assert.That(synchronous.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await NativeBoundLeaseDisposalReports.ChargedAsync(operation.Owner, parent.RequestId);
        var disposal = NativeBoundLeaseDisposalReports.Begin(parent, original, cleanup, completionFirst);
        parent.PrepareDisposal(new InvalidOperationException(nameof(ExecuteAsync)), new IOException(nameof(ExecuteAsync)));
        await Assert.That(parent.DisposeAsync().AsTask()).IsSameReferenceAs(disposal);
        var completion = parent.CompleteAsync(new InvalidOperationException(nameof(NativeBoundLeaseDisposalOperation)), null);
        await Assert.That<object>(completion).IsSameReferenceAs(parent.Settlement);
        await Assert.That(disposal).IsSameReferenceAs(parent.Settlement);
        await disposal.WaitAsync(operation.Token);
        var outcome = await parent.Settlement;
        await Assert.That(outcome.OriginalFailure).IsSameReferenceAs(original);
        await NativeBoundLeaseDisposalReports.CleanupAsync(outcome, cleanup);
        await Assert.That(await completion).IsSameReferenceAs(outcome);
        await Assert.That(parent.DisposeAsync().AsTask()).IsSameReferenceAs(disposal);
        await NativeBoundLeaseAssertions.RetentionAsync(operation, input.Coverage.Lifetimes.Single());
    }
}
