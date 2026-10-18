using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationCold
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueViewLeaseIsolationState state)
    {
        var failures = new List<Exception>();
        for (var cut = QueueViewLeaseIsolationProtocol.Empty; cut < QueueViewLeaseIsolationProtocol.ColdCuts && failures.Count == QueueViewLeaseIsolationProtocol.Empty; cut++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var deadline = McpCallerDeadline.Create();
                await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                await WaveAsync(fixture, state, deadline.Token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<bool> WaveAsync(ClusterFixture fixture, QueueViewLeaseIsolationState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                await SqlClusterProtocol.EqualAsync(state.Terminal
                    ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent),
                    await QueueViewLeaseIsolationImages.ReadAsync(root, state, token));
                await QueueViewLeaseIsolationReads.RunAsync(root, reader, state, token);
                await QueueViewLeaseIsolationHistory.RunAsync(root, state, token);
                await QueueViewLeaseIsolationRefusals.InputsAsync(root, reader, state, token);
                return true;
            }, token), token);
}
