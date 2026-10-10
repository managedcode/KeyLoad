using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventAppendRf3Calls
{
    internal static async Task<EventAppendRf3Outcome> SdkAsync(KeyLoadClient sdk, CommandRequest command,
        CancellationToken token)
    {
        var result = await sdk.CommitAsync(command, token).ConfigureAwait(false);
        if (result.IsSuccess)
        { return new(await McpCallerAssertions.SdkSuccessAsync(result), null, null, null); }
        var problem = JsonSerializer.SerializeToElement(result.Problem, JsonDefaults.Options);
        var code = ReadCode(problem);
        await McpCallerAssertions.VerifyProblemAsync(problem, code);
        return new(null, code, problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString(),
            JsonSerializer.Serialize(problem, JsonDefaults.Options));
    }

    internal static async Task<EventAppendRf3Outcome> McpAsync(McpOfficialClient official, CommandRequest command,
        CancellationToken token)
    {
        var result = await official.CallAsync(McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false);
        if (result.IsError is not true)
        { return new((await McpCallerAssertions.SuccessAsync<CommitReceipt>(result)).Value, null, null, null); }
        var problem = (result.StructuredContent ?? throw new InvalidOperationException(EventAppendRf3Protocol.MissingProblem))
            .GetProperty(McpCallerProtocol.Error);
        var code = ReadCode(problem);
        await McpCallerAssertions.ErrorAsync(result, code, dispatched: true);
        return new(null, code, problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString(),
            JsonSerializer.Serialize(problem, JsonDefaults.Options));
    }

    internal static async Task<(EventAppendRf3Outcome Sdk, EventAppendRf3Outcome Mcp)> ConcurrentAsync(
        KeyLoadClient sdk, McpOfficialClient official, CommandRequest first, CommandRequest second, CancellationToken token)
    {
        var sdkTask = SdkAsync(sdk, first, token);
        var mcpTask = McpAsync(official, second, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Task.WhenAll(sdkTask, mcpTask), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return (await sdkTask.ConfigureAwait(false), await mcpTask.ConfigureAwait(false));
    }

    private static ErrorCode ReadCode(JsonElement problem)
    {
        var text = problem.GetProperty(McpCallerProtocol.ProblemCode).GetString();
        if (!Enum.TryParse<ErrorCode>(text, out var code) || !Enum.IsDefined(code)
            || !string.Equals(text, code.ToString(), StringComparison.Ordinal))
        { throw new InvalidOperationException(EventAppendRf3Protocol.MissingProblem); }
        return code;
    }
}
