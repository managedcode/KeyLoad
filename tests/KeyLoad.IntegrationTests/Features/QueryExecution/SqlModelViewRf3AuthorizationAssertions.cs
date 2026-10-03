using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlModelViewRf3AuthorizationAssertions
{
    private const int EpochIncrement = 1;

    internal static QueryRequest EventPrivateProjection(SqlModelViewRf3Scenario scenario)
        => scenario.EventSql() with
        {
            Sql = $"SELECT payload.secret AS secret FROM EVENTS('{SqlModelViewRf3Scenario.StreamSet}',"
                + $"'{SqlModelViewRf3Scenario.StreamId}') WHERE id = '{SqlModelViewRf3Scenario.FirstEventId}' LIMIT 1"
        };

    internal static QueryRequest QueuePrivateProjection(SqlModelViewRf3Scenario scenario)
        => scenario.QueueSql() with
        {
            Sql = $"SELECT payload.secret AS secret FROM QUEUE_MESSAGES('{SqlModelViewRf3Scenario.Queue}') "
                + $"WHERE id = '{SqlModelViewRf3Scenario.FirstMessageId}' LIMIT 1"
        };

    internal static async Task GrantSensitiveReadAsync(KeyLoadClient administrator,
        McpPersistedIdentity identity, CancellationToken cancellationToken)
    {
        var updated = identity.Principal with
        { FieldGrants = [SqlModelViewRf3Scenario.SecretGrant, SqlModelViewRf3Scenario.SecretUseGrant] };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            updated, cancellationToken));
    }

    internal static async Task RevokeAsync(KeyLoadClient administrator, McpPersistedIdentity identity,
        CancellationToken cancellationToken)
    {
        var revoked = identity.Principal with
        {
            Grants = [],
            Revoked = true,
            PolicyEpoch = identity.Principal.PolicyEpoch + EpochIncrement
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            revoked, cancellationToken));
    }

    internal static async Task AssertDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueryRequest request, CancellationToken cancellationToken)
    {
        var sdkResult = await sdk.QueryAsync(request, cancellationToken);
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.QueryExecute, request,
            cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }

    internal static async Task AssertMissingGrantDeniedAsync(ClusterFixture fixture,
        SqlModelViewRf3Scenario scenario, Capability onlyCapability, QueryRequest request,
        CancellationToken cancellationToken)
    {
        var resource = request.Sql.Contains("QUEUE_MESSAGES", StringComparison.Ordinal)
            ? SqlModelViewRf3Scenario.Queue : SqlModelViewRf3Scenario.StreamSet;
        var identity = await McpPersistedIdentity.CreateAsync(fixture,
            scenario.Partition, resource, onlyCapability, cancellationToken);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture,
            McpCallerProtocol.Node3, identity.Secret, cancellationToken);
        var result = await sdk.QueryAsync(request, cancellationToken);
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.QueryExecute,
            request, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }
}
