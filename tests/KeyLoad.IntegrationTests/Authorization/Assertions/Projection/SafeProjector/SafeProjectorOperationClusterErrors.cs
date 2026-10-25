using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class SafeProjectorOperationClusterErrors
{
    private const string PermissionDetail = "The authenticated principal is not permitted to perform this operation.";
    private const string GenericDetail = "The database operation could not be completed.";
    private const string Unsupported = "The safe projector public refusal category is unsupported.";
    internal static async Task SdkAsync<T>(Result<T> actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value is null).IsTrue();
        await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(actual.Problem?.Detail).IsEqualTo(detail);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
        await Assert.That(JsonSerializer.Serialize(actual.Problem, JsonDefaults.Options)
            .Contains(SafeProjectorOperationClusterProtocol.Canary, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task McpAsync(CallToolResult actual, ErrorCode code,
        SafeProjectorOperationClusterState state)
    {
        _ = (await McpCallerAssertions.ErrorAsync(actual, code, dispatched: true)).Value;
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
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), problem);
        await McpCallerAssertions.DoesNotDiscloseAsync(actual, state.Key, SafeProjectorOperationClusterProtocol.Canary);
    }
}
