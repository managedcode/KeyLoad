using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparationValidationFixture
{
    internal static async Task RunAsync(Func<ResourceRetirementPreparationValidationState, CancellationToken, Task> exercise)
    {
        var nodes = new List<ControlledPartitionMovementNode>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = Zero; index < Voters; index++)
            {
                nodes.Add(new(Source.Owner));
            }
            for (var index = Zero; index < Voters; index++)
            {
                nodes.Add(new(Target.Owner));
            }
            var token = TestContext.Current!.Execution.CancellationToken;
            var state = await ResourceRetirementPreparationValidationSeed.CreateAsync(nodes, token);
            await exercise(state, token);
        }, failures);
        foreach (var node in nodes.AsEnumerable().Reverse())
        {
            ServerFailureObserver.Observe(node.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static ReadExecutionBudget Work(ControlledPartitionMovementNode node, CancellationToken token)
        => new(UnitExecutionOptions.DatabaseLimits(node.Database.Limits), node.Database.EvaluationClock, token);
}
