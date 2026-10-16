using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AnnLifetimeRf3Assertions
{
    internal const string MissingProducer = "The original ANN lifetime read producer is absent.";
    private const string SourceChanged = "The native ANN remote read scope is no longer current.";
    private const string Cancelled = "The read response is unavailable.";

    internal static async Task OriginalAsync<T>(Task<Result<T>>? sdk, Task<CallToolResult>? mcp,
        CancellationTokenSource caller, bool cancel)
    {
        if (cancel)
        {
            await Assert.That(caller.IsCancellationRequested).IsTrue();
        }
        if (sdk is { } original)
        {
            var result = await original;
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Value).IsNull();
            var problem = result.Problem ?? throw new InvalidOperationException(MissingProducer);
            await Assert.That(problem.ErrorCode).IsEqualTo((cancel ? ErrorCode.Cancelled : ErrorCode.TokenInvalidated).ToString());
            await Assert.That(problem.Detail).IsEqualTo(cancel ? Cancelled : SourceChanged);
            return;
        }
        var actual = mcp ?? throw new InvalidOperationException(MissingProducer);
        try
        {
            var result = await actual;
            await McpCallerAssertions.ErrorAsync(result, cancel ? ErrorCode.Cancelled : ErrorCode.TokenInvalidated, dispatched: true);
            if (cancel)
            {
                await Assert.That(caller.IsCancellationRequested).IsTrue();
            }
        }
        catch (OperationCanceledException) when (cancel && caller.IsCancellationRequested) { }
        catch (HttpRequestException failure) when (cancel && caller.IsCancellationRequested && failure.StatusCode is null) { }
        catch (IOException) when (cancel && caller.IsCancellationRequested) { }
    }
}
