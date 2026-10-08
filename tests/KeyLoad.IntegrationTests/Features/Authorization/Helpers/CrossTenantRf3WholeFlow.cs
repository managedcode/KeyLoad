using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CrossTenantRf3WholeFlow
{
    internal const string Index = "secret";
    internal const string HealthyJson = "{\"public\":\"healthy\",\"secret\":\"kl015-healthy-canary\"}";
    internal const string HealthyCanary = "kl015-healthy-canary";
    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var owned = await SeedAsync(fixture, admin, deadline.Token);
        var foreign = await SeedAsync(fixture, admin, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, owned.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query, deadline.Token);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        McpOfficialClient? session = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, identity.Secret, deadline.Token);
            await RejectReadsAsync(sdk, session, foreign, identity.Secret, deadline.Token);
            await CrossTenantRf3StateAssertions.StateAsync(admin, owned, McpDocumentProtocol.InitialJson, 1, deadline.Token);
            await CrossTenantRf3StateAssertions.StateAsync(admin, foreign, McpDocumentProtocol.InitialJson, 1, deadline.Token);
            var denied = foreign.Command(Guid.NewGuid(), HealthyJson);
            await RejectWritesAsync(sdk, session, denied, identity.Secret, deadline.Token);
            await CrossTenantRf3StateAssertions.StateAsync(admin, foreign, McpDocumentProtocol.InitialJson, 1, deadline.Token);
            await CrossTenantRf3StateAssertions.StateAsync(admin, owned, McpDocumentProtocol.InitialJson, 1, deadline.Token);
            await CrossTenantRf3HealthyFlow.VerifyAsync(sdk, session, owned, identity.Secret, deadline.Token);
            await CrossTenantRf3StateAssertions.StateAsync(admin, foreign, McpDocumentProtocol.InitialJson, 1, deadline.Token);
        }, failures);
        if (session is not null)
        { await ServerFailureObserver.ObserveAsync(() => session.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<McpDocumentScenario> SeedAsync(ClusterFixture fixture, KeyLoadClient admin, CancellationToken token)
    {
        var partition = new PartitionRef(McpDocumentProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            McpDocumentProtocol.Database, McpDocumentProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var scenario = new McpDocumentScenario(partition);
        var resource = new ResourceDefinition(McpDocumentProtocol.Collection, ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        { Indexes = [new(Index, ["/secret"])] };
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, resource), token));
        await scenario.SeedAsync(fixture, token);
        return scenario;
    }
    private static async Task RejectReadsAsync(KeyLoadClient sdk, McpOfficialClient mcp, McpDocumentScenario foreign,
        string secret, CancellationToken token)
    {
        await CrossTenantRf3ErrorAssertions.SdkAsync(await sdk.GetAsync(foreign.Reference, token), secret);
        await CrossTenantRf3ErrorAssertions.McpAsync(await mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(foreign.Reference), token), secret);
        foreach (var indexed in new[] { false, true })
        {
            var request = CrossTenantRf3StateAssertions.Query(foreign.Partition, indexed);
            await CrossTenantRf3ErrorAssertions.SdkAsync(await sdk.QueryAsync(request, token), secret);
            await CrossTenantRf3ErrorAssertions.McpAsync(await mcp.CallAsync(McpCallerTools.QueryExecute, request, token), secret);
        }
    }
    private static async Task RejectWritesAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest request,
        string secret, CancellationToken token)
    {
        await CrossTenantRf3ErrorAssertions.SdkAsync(await sdk.CommitAsync(request, token), secret);
        await CrossTenantRf3ErrorAssertions.McpAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token), secret);
        await CrossTenantRf3ErrorAssertions.SdkAsync(await sdk.CommitAsync(request, token), secret);
        var changed = request with
        {
            Mutations = [new PutDocument(McpDocumentProtocol.Collection,
            McpDocumentProtocol.Entity, McpDocumentProtocol.ConflictingJson)]
        };
        await CrossTenantRf3ErrorAssertions.SdkAsync(await sdk.CommitAsync(changed, token), secret);
        await CrossTenantRf3ErrorAssertions.McpAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, changed, token), secret);
    }
}
