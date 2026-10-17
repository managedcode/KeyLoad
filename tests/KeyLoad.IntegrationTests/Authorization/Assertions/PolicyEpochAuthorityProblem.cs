using System.Net;
using System.Text;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochAuthorityProblem
{
    private const string NoLeader = "The cluster has no current leader with a reachable majority.";
    private const string BodyMarker = "Response body: ";
    private const string ProblemType = "urn:keyload:error:OwnershipLost";
    private const int MaximumFailureBytes = 1024;
    private const int ProblemFields = 5;
    private const int ServiceUnavailable = 503;
    private const int FirstCharacter = 0;

    internal static async Task SdkAsync<T>(ManagedCode.Communication.Result<T> result, string credential)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        if (result.Value is JsonElement value)
        {
            await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        }
        else
        {
            await Assert.That(result.Value).IsNull();
        }
        await Assert.That(result.Problem?.Type).IsEqualTo(ProblemType);
        await Assert.That(result.Problem?.Title).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        await Assert.That(result.Problem?.Detail).IsEqualTo(NoLeader);
        await Assert.That(result.Problem?.StatusCode).IsEqualTo(ServiceUnavailable);
        await SafeAsync(Encoding.UTF8.GetString(JsonDefaults.Serialize(result.Problem)), credential);
    }

    internal static async Task OfficialAsync(Func<Task<CallToolResult>> call, string credential)
    {
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => call())
            ?? throw new InvalidOperationException("The official cursor caller lost its original no-authority failure.");
        await Assert.That(error.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await SafeAsync(error.Message, credential);
        var start = error.Message.IndexOf(BodyMarker, StringComparison.Ordinal);
        await Assert.That(start >= FirstCharacter).IsTrue();
        using var body = JsonDocument.Parse(error.Message[(start + BodyMarker.Length)..]);
        await ProblemAsync(body.RootElement);
    }

    private static async Task SafeAsync(string text, string credential)
    {
        await Assert.That(Encoding.UTF8.GetByteCount(text) <= MaximumFailureBytes).IsTrue();
        await Assert.That(text.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(ResourcePolicyUpdateRf3Protocol.SecretValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(ResourcePolicyUpdateRf3Protocol.SeedJson, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(PolicyEpochPublicProtocol.SecondJson, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task ProblemAsync(JsonElement body)
    {
        await Assert.That(body.ValueKind).IsEqualTo(JsonValueKind.Object);
        var fields = body.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(fields.Length).IsEqualTo(ProblemFields);
        await Assert.That(new HashSet<string>(fields, StringComparer.Ordinal).SetEquals(McpCallerAssertions.ProblemFields)).IsTrue();
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemType).GetString()).IsEqualTo(ProblemType);
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemTitle).GetString()).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemStatus).GetInt32()).IsEqualTo(ServiceUnavailable);
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(NoLeader);
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemCode).GetString()).IsEqualTo(nameof(ErrorCode.OwnershipLost));
    }
}
