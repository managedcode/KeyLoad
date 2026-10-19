using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.StorageReplication.DocumentSessionReadClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

internal static class DocumentSessionReadClusterAuthorization
{
    internal static async Task VerifyAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        EntityRef reference, CommitToken minimum, McpPersistedIdentity identity, CancellationToken token)
    {
        var revoked = identity.Principal with
        {
            Grants = [],
            PolicyEpoch = identity.Principal.PolicyEpoch + First
        };
        var restored = identity.Principal with
        {
            PolicyEpoch = revoked.PolicyEpoch + First
        };
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DeniedAsync(admin, sdk, mcp,
            reference, minimum, identity, revoked, token), failures);
        var repaired = false;
        using (var cleanup = new CancellationTokenSource(RequestCqrsClusterProtocol.CleanupDeadline, TimeProvider.System))
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(
                    Guid.NewGuid(), restored, cleanup.Token));
                await SqlClusterProtocol.EqualAsync(restored, actual);
                repaired = true;
            }, failures);
        }
        if (repaired)
        {
            await ServerFailureObserver.ObserveAsync(() => DocumentSessionReadClusterAssertions.HealthyAsync(
                sdk, mcp, reference, minimum, FirstJson, First, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DeniedAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        EntityRef reference, CommitToken minimum, McpPersistedIdentity identity, PrincipalRecord revoked,
        CancellationToken token)
    {
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token));
        var denied = await sdk.GetAsync(reference, minimum, token);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.StatusCode).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
        var official = await mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, identity.Secret, FirstJson);
        var call = SqlClusterProtocol.Call(reference.Partition, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference, minimum));
        var sql = await sdk.ExecuteSqlAsync(call, token);
        await Assert.That(sql.IsSuccess).IsFalse();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(sql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That(sql.Problem?.StatusCode).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
        var sqlOfficial = await mcp.CallAsync(SqlOperationProtocol.ToolName, call, token);
        await McpCallerAssertions.ErrorAsync(sqlOfficial, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(sqlOfficial, identity.Secret, FirstJson);
        var unchanged = await PublicReadAssertions.DocumentAsync(await admin.GetAsync(reference, minimum, token));
        var expected = new DocumentResult(reference, First, FirstJson, false, []);
        await Assert.That(JsonDefaults.Serialize(unchanged).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
