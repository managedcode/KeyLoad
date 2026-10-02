using System.Net;
using System.Text.Json;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AdminDashboardRf3Tests(ClusterFixture fixture)
{
    private const string AdminPath = "/admin";
    private const string TraversalPath = "/admin/unknown.js";

    [Test]
    public async Task AcAd007RealSdkAndOfficialMcpAgreeOnCatalogAndNonconsumingQueue()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AdminDashboardScenario.CreateAsync(fixture, deadline.Token);
        var sdk = fixture.Client(McpCallerProtocol.Node1);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        var catalog = await McpCallerAssertions.SdkSuccessAsync(await sdk.ListResourcesAsync(scenario.Resources, deadline.Token));
        var catalogMcp = await McpCallerAssertions.SuccessAsync<AdminResourcesPage>(await mcp.CallAsync(
            AdminDashboardProtocol.ResourcesTool, scenario.Resources, deadline.Token));
        await Assert.That(catalog.Items.SequenceEqual(catalogMcp.Value.Items)).IsTrue();
        var queue = await McpCallerAssertions.SdkSuccessAsync(await sdk.BrowseQueueAsync(scenario.Messages, deadline.Token));
        var queueMcp = await McpCallerAssertions.SuccessAsync<AdminQueuePage>(await mcp.CallAsync(
            AdminDashboardProtocol.QueueTool, scenario.Messages, deadline.Token));
        await Assert.That(queue.Counters).IsEqualTo(queueMcp.Value.Counters);
        await Assert.That(queue.Items.SequenceEqual(queueMcp.Value.Items)).IsTrue();
        await Assert.That(queue.CutPosition).IsEqualTo(queueMcp.Value.CutPosition);
        await Assert.That(queue.Items[0].State).IsEqualTo(MessageState.Ready);
        await Assert.That(queue.Counters.StoredMessages).IsEqualTo(1);
        await Assert.That(catalogMcp.RequestId).IsNotEqualTo(queueMcp.RequestId);
        await Assert.That(JsonSerializer.Serialize(queueMcp.Value).Contains(AdminDashboardScenario.PrivatePayload,
            StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcAd002ActualPhysicalNodesAndExcludedPollingHaveHonestSnapshots()
    {
        using var deadline = McpCallerDeadline.Create();
        foreach (var node in new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 })
        {
            var sdk = fixture.Client(node);
            var first = await McpCallerAssertions.SdkSuccessAsync(await sdk.DashboardAsync(deadline.Token));
            var second = await McpCallerAssertions.SdkSuccessAsync(await sdk.DashboardAsync(deadline.Token));
            var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(deadline.Token));
            await Assert.That(first.Node.NodeId).IsEqualTo(status.NodeId);
            await Assert.That(Guid.TryParse(first.Node.NodeId, out var nodeIdentity) && nodeIdentity != Guid.Empty).IsTrue();
            await Assert.That(first.Node.Incarnation).IsEqualTo(status.Incarnation);
            await Assert.That(first.Node.Voters).IsEqualTo(3);
            await Assert.That(first.Http.ProcessInstance).IsEqualTo(second.Http.ProcessInstance);
            await Assert.That(first.Http.CompletedRequests).IsEqualTo(second.Http.CompletedRequests);
            await Assert.That(first.Storage.Files.Length).IsLessThanOrEqualTo(200);
            await Assert.That(first.Storage.Files.All(file => !Path.IsPathRooted(file.Path) && file.Bytes >= 0)).IsTrue();
            await Assert.That(first.Storage.Complete || !string.IsNullOrWhiteSpace(first.Storage.Notice)).IsTrue();
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, fixture.AdminKey, deadline.Token);
            var native = await McpCallerAssertions.SuccessAsync<AdminNodeSnapshot>(await mcp.Client.CallToolAsync(
                AdminDashboardProtocol.SnapshotTool, cancellationToken: deadline.Token));
            await Assert.That(native.Value.Node.NodeId).IsEqualTo(first.Node.NodeId);
            await Assert.That(native.Value.Node.Incarnation).IsEqualTo(first.Node.Incarnation);
            await Assert.That(native.Value.Http.ProcessInstance).IsEqualTo(first.Http.ProcessInstance);
        }
    }

    [Test]
    public async Task AcAd001CredentialFreeShellNeverExemptsDataOrUnknownAssets()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = fixture.App.CreateHttpClient(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint);
        using var shell = await http.GetAsync(new Uri(AdminPath, UriKind.Relative), deadline.Token);
        await Assert.That(shell.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var data = await http.GetAsync(new Uri(AdminDashboardProtocol.SnapshotPath, UriKind.Relative), deadline.Token);
        await Assert.That(data.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var unknown = await http.GetAsync(new Uri(TraversalPath, UriKind.Relative), deadline.Token);
        await Assert.That(unknown.StatusCode == HttpStatusCode.NotFound || unknown.StatusCode == HttpStatusCode.Unauthorized).IsTrue();
        var scenario = await AdminDashboardScenario.CreateAsync(fixture, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            AdminDashboardScenario.Collection, Capability.All, deadline.Token);
        var member = fixture.Client(McpCallerProtocol.Node2, identity.Secret);
        await Assert.That((await member.ListResourcesAsync(scenario.Resources, deadline.Token)).Problem?.ErrorCode)
            .IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That((await member.DashboardAsync(deadline.Token)).IsFailed).IsTrue();
    }

    [Test]
    public async Task AcAd003RealDocumentSuccessAndFailureAdvanceProcessHttpObservations()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AdminDashboardScenario.CreateAsync(fixture, deadline.Token);
        var sdk = fixture.Client(McpCallerProtocol.Node1);
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.DashboardAsync(deadline.Token));
        await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(new(scenario.Partition,
            AdminDashboardScenario.Collection, AdminDashboardScenario.DocumentId), deadline.Token));
        var failed = await sdk.GetAsync(new(scenario.Partition, AdminDashboardScenario.Queue,
            AdminDashboardScenario.DocumentId), deadline.Token);
        await Assert.That(failed.IsFailed).IsTrue();
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.DashboardAsync(deadline.Token));
        await Assert.That(after.Http.ProcessInstance).IsEqualTo(before.Http.ProcessInstance);
        await Assert.That(after.Http.CompletedRequests).IsGreaterThanOrEqualTo(before.Http.CompletedRequests + 2);
        await Assert.That(after.Http.FailedRequests).IsGreaterThanOrEqualTo(before.Http.FailedRequests + 1);
        await Assert.That(after.Http.ElapsedMilliseconds).IsGreaterThan(before.Http.ElapsedMilliseconds);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAd001CurrentPersistedAdministratorRevocationOrExpiryRejectsLaterReads(bool expire)
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AdminDashboardScenario.CreateAsync(fixture, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            AdminDashboardScenario.Collection, Capability.All, deadline.Token);
        var root = fixture.Client(McpCallerProtocol.Node1);
        var administrator = identity.Principal with { ClusterAdministrator = true };
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), administrator, deadline.Token));
        var sdk = fixture.Client(McpCallerProtocol.Node2, identity.Secret);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.ListResourcesAsync(scenario.Resources, deadline.Token));
        var disabled = administrator with
        {
            Revoked = !expire,
            ExpiresAt = expire ? TimeProvider.System.GetUtcNow().AddMinutes(-1) : null,
            PolicyEpoch = administrator.PolicyEpoch + 1
        };
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), disabled, deadline.Token));
        var result = await sdk.ListResourcesAsync(scenario.Resources, deadline.Token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        await Assert.That(JsonSerializer.Serialize(result.Problem).Contains(identity.Secret, StringComparison.Ordinal)).IsFalse();
    }
}
