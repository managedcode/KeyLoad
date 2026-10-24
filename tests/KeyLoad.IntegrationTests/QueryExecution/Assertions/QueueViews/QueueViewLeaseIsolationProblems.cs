using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueueViewLeaseIsolationProblems
{
    internal static async Task SdkAsync<T>(Result<T> result, ErrorCode code)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value is null).IsTrue();
        await Assert.That(result.Problem?.ErrorCode == code.ToString()).IsTrue();
    }

    internal static async Task SqlAsync(Result<JsonElement> result, ErrorCode code)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(result.Problem?.ErrorCode == code.ToString()).IsTrue();
    }

    internal static async Task McpAsync(CallToolResult result, QueueViewLeaseIsolationState state, ErrorCode code, bool dispatched = true)
    {
        await McpCallerAssertions.ErrorAsync(result, code, dispatched);
        await McpCallerAssertions.DoesNotDiscloseAsync(result, state.Reader.Secret, SqlModelViewClusterScenario.QueueCanary);
        await McpCallerAssertions.DoesNotDiscloseAsync(result, state.Reader.Secret, SqlModelViewClusterScenario.QueueHeaderCanary);
        await McpCallerAssertions.DoesNotDiscloseAsync(result, state.Reader.Secret, state.Delivery.Token);
    }
}
