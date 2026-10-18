using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Owns six actual independent local native stores, without claiming replica consensus or CLUSTER transport.</summary>
internal static class ResourceRetirementOriginal8RecoveryFixture
{
    internal static async Task RunAsync(Func<IReadOnlyList<ControlledPartitionMovementNode>, CancellationToken, Task> exercise)
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
            try
            {
                await exercise(nodes, TestContext.Current!.Execution.CancellationToken);
            }
            catch (Exception)
            {
                foreach (var node in nodes)
                {
                    node.RetainRoot = true;
                }
                throw;
            }
        }, failures);
        foreach (var node in nodes.AsEnumerable().Reverse())
        {
            ServerFailureObserver.Observe(node.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
