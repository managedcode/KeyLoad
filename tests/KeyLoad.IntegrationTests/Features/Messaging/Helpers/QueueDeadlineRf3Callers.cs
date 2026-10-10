using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineRf3Callers
{
    internal static async Task<T> UseAsync<T>(ClusterFixture fixture, string node, string credential,
        Func<RequestCqrsRf3Callers, Task<T>> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        T? result = default;
        try
        {
            await using var callers = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, node, credential, token);
            try
            { result = await operation(callers); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }
}
