using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Deadline
{
    internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation)
    {
        var failures = new List<Exception>();
        T result = default!;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                result = await operation(deadline.Token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}
