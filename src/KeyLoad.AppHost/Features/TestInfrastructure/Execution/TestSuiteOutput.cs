using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteOutput
{
    internal static async Task ForwardAsync(DistributedApplication app, string resource, CancellationToken token)
    {
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        try
        {
            await foreach (var batch in logs.WatchAsync(resource).WithCancellation(token).ConfigureAwait(false))
            {
                await WriteAsync(batch).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static async Task WriteAsync(IEnumerable<LogLine> lines)
    {
        foreach (var line in lines)
        {
            var writer = line.IsErrorMessage ? Console.Error : Console.Out;
            await writer.WriteLineAsync(line.Content).ConfigureAwait(false);
        }
    }
}
