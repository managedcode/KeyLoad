using KeyLoad.Clustering;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDeadlineClusterCallers
{
    internal static async Task<T> UseAsync<T>(ClusterFixture fixture, string node, string credential,
        Func<RequestCqrsClusterCallers, Task<T>> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        T? result = default;
        try
        {
            await using var callers = await RequestCqrsClusterCallers.ConnectAsync(fixture.App, node, credential, token);
            try
            {
                result = await operation(callers);
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }
}
