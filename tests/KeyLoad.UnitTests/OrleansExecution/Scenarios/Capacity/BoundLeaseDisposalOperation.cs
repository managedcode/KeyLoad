namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class BoundLeaseDisposalOperation
{
    internal static async Task ExecuteAsync(BoundLeaseSeeded state,
        BoundLeaseOperation operation, Exception? original, Exception? cleanup, bool completionFirst)
    {
        var input = BoundLeaseQuery.Create(state, operation);
        await BoundLeaseAuthorization.ReceivingTenantAsync(state, operation, input);
        await BoundLeaseAssertions.QueryAsync(state, input,
            BoundLeaseExecution.Read(state, operation, input));
        operation.Parent.AdmitPublication();
        await BoundLeaseAssertions.RetentionAsync(operation, input.Coverage.Lifetimes.Single());
        var parent = operation.Parent;
        var synchronous = Assert.ThrowsExactly<KeyLoadException>(parent.Dispose);
        await Assert.That(synchronous.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await BoundLeaseDisposalReports.ChargedAsync(operation.Owner, parent.RequestId);
        var disposal = BoundLeaseDisposalReports.Begin(parent, original, cleanup, completionFirst);
        parent.PrepareDisposal(new InvalidOperationException(nameof(ExecuteAsync)), new IOException(nameof(ExecuteAsync)));
        await Assert.That(parent.DisposeAsync().AsTask()).IsSameReferenceAs(disposal);
        var completion = parent.CompleteAsync(new InvalidOperationException(nameof(BoundLeaseDisposalOperation)), null);
        await Assert.That<object>(completion).IsSameReferenceAs(parent.Settlement);
        await Assert.That(disposal).IsSameReferenceAs(parent.Settlement);
        await disposal.WaitAsync(operation.Token);
        var outcome = await parent.Settlement;
        await Assert.That(outcome.OriginalFailure).IsSameReferenceAs(original);
        await BoundLeaseDisposalReports.CleanupAsync(outcome, cleanup);
        await Assert.That(await completion).IsSameReferenceAs(outcome);
        await Assert.That(parent.DisposeAsync().AsTask()).IsSameReferenceAs(disposal);
        await BoundLeaseAssertions.RetentionAsync(operation, input.Coverage.Lifetimes.Single());
    }
}
