using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BackupRestore.Helpers;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>AC-CLIENT-004/006: admin backup has typed parity across the real public callers.</summary>
/// <param name="fixture">The actual Aspire-owned Docker RF3 application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AdminBackupClientParityTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcClient004SdkAndOfficialMcpCreateDistinctAdministratorBackups()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var seed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            scenario.Command(Guid.NewGuid()), deadline.Token));
        await Assert.That(seed.Token.Position).IsGreaterThan(0);
        var seededMutation = seed.Mutations.Single(mutation => mutation.Resource == McpDocumentProtocol.Collection
            && mutation.Id == McpDocumentProtocol.Entity);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, deadline.Token);
        var tool = await mcp.Client.DiscoverKeyLoadToolAsync(McpCallerTools.AdminBackup, deadline.Token);
        await McpDiscoveryAssertions.VerifyAsync(tool);
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.BackupAsync(deadline.Token));
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<BackupReceipt>(
            await mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.AdminBackup, cancellationToken: deadline.Token));
        await VerifyReceiptAsync(sdkReceipt);
        await VerifyReceiptAsync(mcpReceipt.Value);
        await Assert.That(sdkReceipt.Id).IsNotEqualTo(mcpReceipt.Value.Id);
        await AdminBackupArchiveVerifier.VerifyRestoreAsync(fixture, sdkReceipt, scenario.Reference,
            McpDocumentProtocol.InitialJson, seededMutation.Revision, seed.Token.Position, deadline.Token);
        await AdminBackupArchiveVerifier.VerifyRestoreAsync(fixture, mcpReceipt.Value, scenario.Reference,
            McpDocumentProtocol.InitialJson, seededMutation.Revision, seed.Token.Position, deadline.Token);
    }

    private static async Task VerifyReceiptAsync(BackupReceipt receipt)
    {
        var validId = Guid.TryParseExact(receipt.Id, McpCallerProtocol.GuidFormat, out var id) && id != Guid.Empty;
        await Assert.That(validId).IsTrue();
        await Assert.That(id.ToString(McpCallerProtocol.GuidFormat)).IsEqualTo(receipt.Id);
        await Assert.That(receipt.Position).IsGreaterThanOrEqualTo(0);
    }
}
