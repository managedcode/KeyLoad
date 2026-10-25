using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class RequiredProcessingPolicyFlow
{
    internal static async Task RunAsync(bool header, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new RequiredProcessingPolicyFixture(header);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync(token);
                var first = await RequiredProcessingPolicyProcessing.ReceiveAsync(fixture, "first", token);
                await fixture.PolicyAsync(RequiredProcessingPolicyFixture.After, token);
                await RequiredProcessingPolicyReads.LeasedAsync(fixture, first, true, token);
                var refused = fixture.Processing(Guid.NewGuid(), first, "first");
                var operation = fixture.Operation(OperationKind.Processing, refused, refused.CommandId);
                var failureBytes = await RequiredProcessingPolicyRefusal.FreshAsync(fixture, operation, ErrorCode.PermissionDenied,
                    RequiredProcessingPolicyFixture.Denied, token);
                await RequiredProcessingPolicyReads.LeasedAsync(fixture, first, true, token);
                await fixture.PolicyAsync(RequiredProcessingPolicyFixture.Before, token);
                await RequiredProcessingPolicyRefusal.ExistingAsync(fixture.Harness.Database, fixture.Harness.Store, fixture.Harness.Partition,
                    operation, failureBytes, ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.Denied, token);
                await RequiredProcessingPolicyReads.LeasedAsync(fixture, first, false, token);
                var successful = await RequiredProcessingPolicyProcessing.CompleteAsync(fixture, fixture.Processing(Guid.NewGuid(), first, "first"), "first", token);
                await fixture.PolicyAsync(RequiredProcessingPolicyFixture.After, token);
                await RequiredProcessingPolicyRefusal.ExistingAsync(fixture.Harness.Database, fixture.Harness.Store, fixture.Harness.Partition,
                    successful.Operation, successful.Outcome, ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.Denied, token);
                await fixture.PolicyAsync(RequiredProcessingPolicyFixture.Before, token);
                await RequiredProcessingPolicyAssertions.ReplayAsync(fixture.Harness.Database, fixture.Harness.Store, successful.Operation,
                    successful.Receipt, successful.Outcome, fixture.Harness.Partition, token);
                await fixture.PrincipalAsync(2L, true, token);
                var revoked = fixture.Processing(Guid.NewGuid(), first, "first");
                var revokedOperation = fixture.Operation(OperationKind.Processing, revoked, revoked.CommandId);
                await RequiredProcessingPolicyAssertions.ObserveAsync(fixture.Harness.Store, async () =>
                {
                    await RequiredProcessingPolicyAssertions.FailureAsync(fixture.Harness.Database.ApplyEmbedded(revokedOperation, token),
                        ErrorCode.Unauthenticated, RequiredProcessingPolicyFixture.Unavailable);
                    await Assert.That(fixture.Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
                        fixture.Harness.Partition, revokedOperation.UserId!, revoked.CommandId)))).IsNull();
                    await Assert.That(fixture.Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocator(
                        fixture.Harness.Partition, revokedOperation.UserId!, revoked.CommandId)))).IsNull();
                });
                await fixture.PrincipalAsync(3L, false, token);
                await RequiredProcessingPolicyRefusal.ExistingAsync(fixture.Harness.Database, fixture.Harness.Store, fixture.Harness.Partition,
                    successful.Operation, successful.Outcome, ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.EpochChanged, token);
                await RequiredProcessingPolicyRefusal.ExistingAsync(fixture.Harness.Database, fixture.Harness.Store, fixture.Harness.Partition,
                    operation, failureBytes, ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.EpochChanged, token);
                await fixture.EnqueueAsync("next", token);
                var next = await RequiredProcessingPolicyProcessing.ReceiveAsync(fixture, "next", token);
                var request = fixture.Processing(Guid.NewGuid(), next, "next");
                await RequiredProcessingPolicyProcessing.CancelledAsync(fixture, request);
                var healthy = await RequiredProcessingPolicyProcessing.CompleteAsync(fixture, request, "next", token);
                await RequiredProcessingPolicyReads.FinalAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token);
                await RequiredProcessingPolicyCold.RunAsync(fixture, (operation, failureBytes), successful, healthy, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
