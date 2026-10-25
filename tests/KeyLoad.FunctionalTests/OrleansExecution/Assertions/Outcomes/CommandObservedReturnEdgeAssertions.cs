using P = KeyLoad.FunctionalTests.OrleansExecution.CommandObservedPolicyProtocol;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class CommandObservedReturnEdgeAssertions
{
    internal static async Task NoOutcomeAsync(CommandObservedPolicyScenario scenario,
        ReceiveRequest request, CommandObservedReturnEdgeSnapshot before, CancellationToken token)
    {
        await before.RequireSameAsync(scenario, token);
        await Assert.That(OutcomeStoreOracle.ReadPartition(scenario.Fixture.Database.Store,
            scenario.Fixture.Database.Partition, P.Worker, request.RequestId)).IsNull();
    }

    internal static async Task<ReceiveResult> OriginalAsync(CommandObservedPolicyScenario scenario,
        ReceiveRequest request, CancellationToken token)
    {
        await scenario.Fixture.Database.Coordinator.ReadBarrierAsync(token);
        var retained = CommandObservedPolicyAssertions.Outcome(scenario, request.RequestId);
        await Assert.That(Errors.Code(retained.Problem).Value).IsNull();
        var value = retained.Get<ReceiveResult>();
        await CommandObservedPolicyAssertions.LiteralAsync(scenario, request, value, P.First);
        return value;
    }

    internal static async Task ReplayAndNextAsync(CommandObservedPolicyScenario scenario,
        ReceiveRequest request, ReceiveResult expected, CancellationToken token)
    {
        var before = await CommandObservedReturnEdgeSnapshot.CaptureAsync(scenario, token);
        var bytes = CommandObservedPolicyAssertions.OutcomeBytes(scenario, request.RequestId);
        var execution = CommandObservedPolicyAssertions.RuntimePayloadBytes(scenario, expected);
        var replay = await CommandObservedPolicyCalls.ReceiveAsync(scenario, Guid.NewGuid(), request, token);
        await CommandObservedPolicyAssertions.EqualAsync(expected,
            CommandObservedPolicyAssertions.Value<ReceiveResult>(replay));
        await before.RequireSameAsync(scenario, token);
        await Assert.That(CommandObservedPolicyAssertions.OutcomeBytes(scenario, request.RequestId)
            .SequenceEqual(bytes)).IsTrue();
        await Assert.That(CommandObservedPolicyAssertions.RuntimePayloadBytes(scenario, expected)
            .SequenceEqual(execution)).IsTrue();
        var next = scenario.Receive();
        var healthy = await CommandObservedPolicyCalls.ReceiveAsync(scenario, Guid.NewGuid(), next, token);
        await scenario.Fixture.Database.Coordinator.ReadBarrierAsync(token);
        await CommandObservedPolicyAssertions.LiteralAsync(scenario, next,
            CommandObservedPolicyAssertions.Value<ReceiveResult>(healthy), P.Second);
    }
}
