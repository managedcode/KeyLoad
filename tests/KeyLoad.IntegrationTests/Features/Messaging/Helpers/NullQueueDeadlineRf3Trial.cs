using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class NullQueueDeadlineRf3Trial
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
                var state = await NullQueueDeadlineRf3Operation.SeedAsync(fixture, lifetime.Token);
                var cancellation = await ObserveRefusalAsync(fixture, state, lifetime.Token);
                await NullQueueDeadlineRf3Operation.ContinueAsync(fixture, state, cancellation, lifetime.Token);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<CommitReceipt> ObserveRefusalAsync(ClusterFixture fixture,
        NullQueueDeadlineRf3State state, CancellationToken token)
    {
        var receipt = await NullQueueDeadlineRf3Observation.WaitAsync(fixture, state, token);
        await NullQueueDeadlineRf3Operation.RequireUnchangedAsync(fixture, McpCallerProtocol.Node2, state, token);
        return receipt;
    }
}
