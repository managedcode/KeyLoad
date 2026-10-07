using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Runs one rejection and healthy follow-up against an owned real Aspire RF3 fixture.</summary>
internal static class RelationalSqlRf3JoinBudgetFlow
{
    private const string NodeForSdk = McpCallerProtocol.Node1;
    private const string NodeForMcp = McpCallerProtocol.Node2;
    private const string OutputCustomerId = "customer_id";
    private const string OutputDetail = "left_detail";
    private const string OutputName = "right_name";
    private const string OutputOrderId = "order_id";
    private const string JoinAccessPath = "bounded-primary-key-inner-join";
    private const int InitialSeedIndex = 1;
    private const long SeedRevision = 1;
    private const string MissingDocument = "The SDK did not return a seeded join source.";

    internal static async Task ExecuteAsync(JoinBudgetKind kind, DatabaseLimits limits)
    {
        var fixture = new ClusterFixture(limits);
        try
        {
            await fixture.InitializeAsync();
            await RunFixtureAsync(fixture, kind);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static async Task RunFixtureAsync(ClusterFixture fixture, JoinBudgetKind kind)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, NodeForSdk);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var rejected = await PrepareAsync(sdk, kind, false, deadline.Token);
        var healthy = await PrepareAsync(sdk, kind, true, deadline.Token);
        var before = await SnapshotAsync(sdk, rejected.Sources, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, NodeForMcp,
            fixture.AdminKey, deadline.Token);

        await RejectBothCallersAsync(sdk, mcp, rejected.Partition, deadline.Token);
        var after = await SnapshotAsync(sdk, rejected.Sources, deadline.Token);
        await AssertUnchangedAsync(before, after);
        await VerifyHealthyBothCallersAsync(sdk, mcp, healthy, deadline.Token);
    }

    private static async Task<JoinBudgetSeed> PrepareAsync(KeyLoadClient sdk, JoinBudgetKind kind,
        bool healthy, CancellationToken token)
    {
        var partition = RelationalSqlRf3JoinBudgetData.Partition(healthy ? "healthy" : "rejected");
        await RelationalSqlRf3JoinBudgetData.ConfigureAsync(sdk, partition, token);
        return await RelationalSqlRf3JoinBudgetData.SeedAsync(sdk, partition, kind, healthy, token);
    }

    private static async Task RejectBothCallersAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, CancellationToken token)
    {
        var request = RelationalSqlRf3JoinBudgetData.Query(partition);
        var sdkFailure = await sdk.QueryAsync(request, token);
        await Assert.That(sdkFailure.IsSuccess).IsFalse();
        await Assert.That(sdkFailure.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        var mcpFailure = await mcp.CallAsync(McpCallerTools.QueryExecute, request, token);
        await McpCallerAssertions.ErrorAsync(mcpFailure, ErrorCode.BudgetExceeded, dispatched: true);
    }

    private static async Task VerifyHealthyBothCallersAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        JoinBudgetSeed healthy, CancellationToken token)
    {
        var request = RelationalSqlRf3JoinBudgetData.Query(healthy.Partition);
        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(request, token));
        var mcpPage = (await McpCallerAssertions.SuccessAsync<QueryPage>(
            await mcp.CallAsync(McpCallerTools.QueryExecute, request, token))).Value;
        await RelationalSqlRf3JoinPageAssertions.EquivalentAsync(sdkPage, mcpPage);
        await VerifyHealthyResultAsync(sdkPage, healthy);
    }

    private static async Task VerifyHealthyResultAsync(QueryPage page, JoinBudgetSeed healthy)
    {
        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(healthy.CommitPosition);
        await Assert.That(page.AccessPath).IsEqualTo(JoinAccessPath);
        await Assert.That(page.Rows[0].EntityId).IsEqualTo(healthy.ExpectedOrderId);
        await RelationalSqlRf3JoinPageAssertions.RevisionsAsync(page, SeedRevision, SeedRevision);
        using var row = JsonDocument.Parse(page.Rows[0].Json);
        await Assert.That(row.RootElement.GetProperty(OutputOrderId).GetString()).IsEqualTo(healthy.ExpectedOrderId);
        await Assert.That(row.RootElement.GetProperty(OutputDetail).GetString()).IsEqualTo(healthy.ExpectedDetail);
        await Assert.That(row.RootElement.GetProperty(OutputName).GetString()).IsEqualTo(healthy.ExpectedName);
        await Assert.That(row.RootElement.GetProperty(OutputCustomerId).GetString())
            .IsEqualTo(healthy.ExpectedCustomerId);
    }

    private static async Task<ImmutableArray<StoredSource>> SnapshotAsync(KeyLoadClient sdk,
        ImmutableArray<EntityRef> references, CancellationToken token)
    {
        var snapshots = ImmutableArray.CreateBuilder<StoredSource>(references.Length);
        foreach (var reference in references)
        {
            var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, token));
            var stored = document ?? throw new InvalidOperationException(MissingDocument);
            await Assert.That(stored.Reference).IsEqualTo(reference);
            snapshots.Add(new(stored.Reference, stored.Revision, stored.Json));
        }
        return snapshots.MoveToImmutable();
    }

    private static async Task AssertUnchangedAsync(ImmutableArray<StoredSource> before,
        ImmutableArray<StoredSource> after)
    {
        await Assert.That(after.Length).IsEqualTo(before.Length);
        for (var index = InitialSeedIndex - 1; index < before.Length; index++)
        {
            await Assert.That(after[index].Reference).IsEqualTo(before[index].Reference);
            await Assert.That(after[index].Revision).IsEqualTo(before[index].Revision);
            await Assert.That(after[index].Json).IsEqualTo(before[index].Json);
        }
    }

    private sealed record StoredSource(EntityRef Reference, long Revision, string Json);
}
