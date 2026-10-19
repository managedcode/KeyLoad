using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class CommandObservedPolicyCalls
{
    internal static Task<GrainOperationReply> ReceiveAsync(CommandObservedPolicyScenario scenario,
        Guid requestId, ReceiveRequest request, CancellationToken token)
        => scenario.Fixture.Database.SubmitAsync(OperationKind.Receive, request, token,
            principal: scenario.Worker.Id, id: request.RequestId, requestId: requestId);
}
