using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class NullQueueDeadlineClusterTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            await using var fixture = new ClusterFixture();
            try
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                var state = await NullQueueDeadlineClusterOperation.SeedAsync(fixture, lifetime.Token);
                var cancellation = await ObserveRefusalAsync(fixture, state, lifetime.Token);
                await NullQueueDeadlineClusterOperation.ContinueAsync(fixture, state, cancellation, lifetime.Token);
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<CommitReceipt> ObserveRefusalAsync(ClusterFixture fixture,
        NullQueueDeadlineClusterState state, CancellationToken token)
    {
        var receipt = await NullQueueDeadlineClusterObservation.WaitAsync(fixture, state, token);
        await NullQueueDeadlineClusterOperation.RequireUnchangedAsync(fixture, McpCallerProtocol.Node2, state, token);
        return receipt;
    }
}
