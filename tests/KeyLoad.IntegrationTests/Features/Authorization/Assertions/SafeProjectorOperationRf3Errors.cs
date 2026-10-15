using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Errors
{
    private const string PermissionDetail = "The authenticated principal is not permitted to perform this operation.";
    private const string GenericDetail = "The database operation could not be completed.";
    private const string Unsupported = "The safe projector public refusal category is unsupported.";
    internal static async Task SdkAsync<T>(Result<T> actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value is null).IsTrue();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
        await Assert.That(JsonSerializer.Serialize(actual.Problem, JsonDefaults.Options)
            .Contains(SafeProjectorOperationRf3Protocol.Canary, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task McpAsync(CallToolResult actual, ErrorCode code,
        SafeProjectorOperationRf3State state)
    {
        await McpCallerAssertions.ErrorAsync(actual, code, dispatched: true);
        var envelope = actual.StructuredContent ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult);
        var problem = envelope.GetProperty(McpCallerProtocol.Error).Deserialize<Problem>(JsonDefaults.Options)
            ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult);
        var detail = code switch
        {
            ErrorCode.PermissionDenied => PermissionDetail,
            ErrorCode.CursorExpired or ErrorCode.TokenInvalidated or ErrorCode.Validation
                or ErrorCode.UnsupportedCapability => GenericDetail,
            _ => throw new InvalidOperationException(Unsupported)
        };
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), problem);
        await McpCallerAssertions.DoesNotDiscloseAsync(actual, state.Key, SafeProjectorOperationRf3Protocol.Canary);
    }
}
