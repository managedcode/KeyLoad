using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventingRequiredClaimFlow
{
    internal static async Task RunAllAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var header in new[] { false, true })
        {
            await ServerFailureObserver.ObserveAsync(() => RunAsync(header, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task RunAsync(bool header, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new RequiredProcessingPolicyFixture(header);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync(token);
                await fixture.PolicyAsync(RequiredProcessingPolicyFixture.After, token);
                await EventingRequiredClaimState.ReadyAsync(fixture, true, token);
                var request = new ReceiveRequest(Guid.NewGuid(), fixture.Lane);
                var operation = fixture.Operation(OperationKind.Receive, request, request.RequestId);
                var original = await RequiredProcessingPolicyRefusal.FreshAsync(fixture, operation,
                    ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.Denied, token);
                await EventingRequiredClaimState.ReadyAsync(fixture, true, token);
                await fixture.PolicyAsync(RequiredProcessingPolicyFixture.Before, token);
                await RequiredProcessingPolicyRefusal.ExistingAsync(fixture.Harness.Database, fixture.Harness.Store,
                    fixture.Harness.Partition, operation, original, ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.Denied, token);
                await EventingRequiredClaimState.ReadyAsync(fixture, false, token);
                await fixture.PrincipalAsync(2L, true, token);
                await RequiredProcessingPolicyRefusal.ExistingAsync(fixture.Harness.Database, fixture.Harness.Store,
                    fixture.Harness.Partition, operation, original, ErrorCode.Unauthenticated, RequiredProcessingPolicyFixture.Unavailable, token);
                await fixture.PrincipalAsync(3L, false, token);
                await RequiredProcessingPolicyRefusal.ExistingAsync(fixture.Harness.Database, fixture.Harness.Store,
                    fixture.Harness.Partition, operation, original, ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.EpochChanged, token);
                var delivery = await RequiredProcessingPolicyProcessing.ReceiveAsync(fixture, "first", token);
                var forged = fixture.Processing(Guid.NewGuid(), delivery, "first") with
                {
                    Token = delivery.Token + ".forged"
                };
                await RequiredProcessingPolicyRefusal.FreshAsync(fixture, fixture.Operation(OperationKind.Processing, forged, forged.CommandId),
                    ErrorCode.TokenInvalidated, "The signed token is invalid.", token);
                await RequiredProcessingPolicyReads.LeasedAsync(fixture, delivery, false, token);
                var healthy = await RequiredProcessingPolicyProcessing.CompleteAsync(fixture,
                    fixture.Processing(Guid.NewGuid(), delivery, "first") with
                    {
                        HandlerScope = "throw new InvalidOperationException(\"HANDLER-INJECTION-CANARY\")"
                    }, "first", token);
                await EventingRequiredClaimInbox.AssertAsync(fixture, fixture.Harness.Store, healthy.Operation, healthy.Receipt);
                await EventingRequiredClaimState.FinalAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token);
                await EventingRequiredClaimCold.RunAsync(fixture, operation, original, healthy, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
