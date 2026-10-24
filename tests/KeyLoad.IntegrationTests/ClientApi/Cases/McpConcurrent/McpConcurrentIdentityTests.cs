using KeyLoad.IntegrationTests.DocumentStorage;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ClientApi;

/// <summary>AC-MCP-002: concurrent real official callers retain independently persisted tenant authority.</summary>
/// <param name="fixture">The initialized actual execution Aspire Orleans application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpConcurrentIdentityTests(ClusterFixture fixture)
{
    private const int FirstDenial = 2;
    private const int SecondDenial = 3;
    private const int OperationCount = 4;

    /// <summary>Actual concurrent HTTP operations never exchange principals between two execution SDK clients.</summary>
    [Test]
    public async Task AcMcp002DistinctOfficialClientsKeepTheirOwnTenantsDuringConcurrentCalls()
    {
        using var deadline = McpCallerDeadline.Create();
        var first = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        var second = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        await first.SeedAsync(fixture, deadline.Token);
        await second.SeedAsync(fixture, deadline.Token);
        var firstIdentity = await McpPersistedIdentity.CreateAsync(fixture, first.Partition, Capability.DocumentsRead, deadline.Token);
        var secondIdentity = await McpPersistedIdentity.CreateAsync(fixture, second.Partition, Capability.DocumentsRead, deadline.Token);
        await using var firstClient = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, firstIdentity.Secret, deadline.Token);
        await using var secondClient = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, secondIdentity.Secret, deadline.Token);
        var before = await global::KeyLoad.IntegrationTests.Authorization.ConcurrentPrincipalAuthorityAssertions.CaptureAsync(
            fixture, first, second, deadline.Token);
        var calls = await Task.WhenAll(
            firstClient.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(first.Reference), deadline.Token),
            secondClient.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(second.Reference), deadline.Token),
            firstClient.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(second.Reference), deadline.Token),
            secondClient.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(first.Reference), deadline.Token));
        var firstRead = await PublicReadAssertions.RequiredDocumentAsync(calls[0]);
        var secondRead = await PublicReadAssertions.RequiredDocumentAsync(calls[1]);
        var firstDenied = await McpCallerAssertions.ErrorAsync(calls[FirstDenial], ErrorCode.PermissionDenied, dispatched: true);
        var secondDenied = await McpCallerAssertions.ErrorAsync(calls[SecondDenial], ErrorCode.PermissionDenied, dispatched: true);
        await global::KeyLoad.IntegrationTests.Authorization.ConcurrentPrincipalAuthorityAssertions.LiteralAsync(first, firstRead.Value);
        await global::KeyLoad.IntegrationTests.Authorization.ConcurrentPrincipalAuthorityAssertions.LiteralAsync(second, secondRead.Value);
        await Assert.That(firstRead.Value.Reference).IsEqualTo(first.Reference);
        await Assert.That(secondRead.Value.Reference).IsEqualTo(second.Reference);
        await Assert.That(new Guid?[] { firstRead.RequestId, secondRead.RequestId, firstDenied, secondDenied }.Distinct().Count())
            .IsEqualTo(OperationCount);
        await McpCallerAssertions.DoesNotDiscloseAsync(calls[FirstDenial], firstIdentity.Secret, McpDocumentProtocol.PrivateValue);
        await McpCallerAssertions.DoesNotDiscloseAsync(calls[SecondDenial], secondIdentity.Secret, McpDocumentProtocol.PrivateValue);
        await SqlClusterProtocol.EqualAsync(before,
            await global::KeyLoad.IntegrationTests.Authorization.ConcurrentPrincipalAuthorityAssertions.CaptureAsync(
                fixture, first, second, deadline.Token));
        await global::KeyLoad.IntegrationTests.Authorization.ConcurrentPrincipalAuthorityAssertions.FollowingAsync(
            firstClient, secondClient, first, second, deadline.Token);
        await SqlClusterProtocol.EqualAsync(before,
            await global::KeyLoad.IntegrationTests.Authorization.ConcurrentPrincipalAuthorityAssertions.CaptureAsync(
                fixture, first, second, deadline.Token));
        await global::KeyLoad.IntegrationTests.Authorization.ConcurrentPrincipalPolicyContinuation.RunAsync(
            fixture, firstClient, secondClient, first, second, firstIdentity, secondIdentity, deadline.Token);
    }
}
