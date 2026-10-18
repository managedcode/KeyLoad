using ManagedCode.Communication;
using System.Net;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationCredentialHttpExportAssertions
{
    internal static async Task ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var actual = JsonDocument.Parse(body);
        using var expected = JsonDocument.Parse(AuthorizationCredentialHttpExportProtocol.ExpectedProblem);
        await Assert.That(JsonElement.DeepEquals(actual.RootElement, expected.RootElement)).IsTrue();
        foreach (var canary in AuthorizationTelemetryTestProtocol.Canaries)
        {
            await Assert.That(body.Contains(canary, StringComparison.Ordinal)).IsFalse();
        }
    }

    internal static async Task<(Guid Id, ConfigureApiKeyRequest Request, Result<object> Result)> ConfigureAsync(TestDatabase database, global::KeyLoad.ApiKeyRecord key)
    {
        var request = new global::KeyLoad.ConfigureApiKeyRequest(key);
        var command = Guid.NewGuid();
        var result = database.Submit(global::KeyLoad.OperationKind.ConfigureApiKey, request, id: command);
        await Assert.That(result.Get<bool>()).IsTrue();
        var stored = database.Store.Read(view => global::KeyLoad.Storage.StorageRecords.GetRecord<global::KeyLoad.ApiKeyRecord>(
            view, global::KeyLoad.Core.KeySpace.ApiKey(key.Id)))
            ?? throw new InvalidOperationException(AuthorizationCredentialHttpExportProtocol.MissingAcknowledgedCredential);
        await Assert.That(global::KeyLoad.NativeSerialization.Serialize(stored).AsSpan()
            .SequenceEqual(global::KeyLoad.NativeSerialization.Serialize(key))).IsTrue();
        var image = AuthorizationTelemetryPolicyRepair.Capture(database);
        var replay = database.Submit(global::KeyLoad.OperationKind.ConfigureApiKey, request, id: command);
        await Assert.That(global::KeyLoad.NativeSerialization.Serialize(replay).AsSpan()
            .SequenceEqual(global::KeyLoad.NativeSerialization.Serialize(result))).IsTrue();
        await AuthorizationTelemetryPolicyRepair.UnchangedAsync(database, image);
        return (command, request, result);
    }
}
