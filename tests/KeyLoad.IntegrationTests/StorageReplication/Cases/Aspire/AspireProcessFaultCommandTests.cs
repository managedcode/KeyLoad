using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;
using KeyLoad.IntegrationTests.EventStreams;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.StorageReplication;

/// <summary>Verifies real resource command refusal without effects, then original SDK/MCP fault recovery.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AspireProcessFaultCommandTests(ClusterFixture fixture)
{
    [Test]
    public async Task WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var scenario = await McpDocumentScenario.CreateAsync(fixture, token);
        await scenario.SeedAsync(fixture, token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var original = ClusterReplicationTestSupport.Success(await sdk.GetAsync(scenario.Reference, token));
        var originalCut = ClusterReplicationTestSupport.Success(await sdk.OutboxStatusAsync(scenario.Partition, token));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, token);
        var resources = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ProjectResource>()
            .Where(resource => resource.Name is McpCallerProtocol.Node1 or McpCallerProtocol.Node2 or McpCallerProtocol.Node3)
            .Select(resource => resource.Name).ToArray();
        await Assert.That(resources).IsEquivalentTo(new[]
            { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 });
        var before = await ReadIdentitiesAsync(resources, token);
        var selected = before[McpCallerProtocol.Node1];
        var rejected = new[]
        {
            selected with { ProcessId = 0 },
            selected with { AspireStartTimeUtcTicks = selected.AspireStartTimeUtcTicks + 1 },
            selected with { ProcessStartTimeUtcTicks = selected.ProcessStartTimeUtcTicks + 1 },
            before[McpCallerProtocol.Node2] with { ResourceName = McpCallerProtocol.Node1 }
        };
        foreach (var rejectedIdentity in rejected)
        {
            await RefuseAsync(rejectedIdentity, token);
            foreach (var resource in resources)
            {
                var observed = await ClusterFixtureApplicationStartup.ReadAsync(fixture.App, resource, token);
                var actual = ProcessRuntimeProtocol.Identity(observed);
                await Assert.That(actual).IsEqualTo(before[resource]);
                var status = ClusterReplicationTestSupport.Success(await fixture.Client(resource).StatusAsync(token));
                await Assert.That(status.ProcessId).IsEqualTo(actual.ProcessId);
            }
            await Assert.That(JsonDefaults.Serialize(ClusterReplicationTestSupport.Success(
                await sdk.GetAsync(scenario.Reference, token))).SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
            await Assert.That(JsonDefaults.Serialize(ClusterReplicationTestSupport.Success(
                await sdk.OutboxStatusAsync(scenario.Partition, token))).SequenceEqual(JsonDefaults.Serialize(originalCut))).IsTrue();
            var publicCut = await McpCallerAssertions.SuccessAsync<OutboxStatus>(await mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(scenario.Partition), token));
            await Assert.That(JsonDefaults.Serialize(publicCut.Value).SequenceEqual(JsonDefaults.Serialize(originalCut))).IsTrue();
            var publicDocument = await PublicReadAssertions.RequiredDocumentAsync(await mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), token));
            await Assert.That(JsonDefaults.Serialize(publicDocument.Value)
                .SequenceEqual(JsonDefaults.Serialize(original.Document))).IsTrue();
        }
        await new AggregateReplayClusterSiloRestartTests(fixture)
            .AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay();
        await Assert.That(JsonDefaults.Serialize(ClusterReplicationTestSupport.Success(
            await sdk.GetAsync(scenario.Reference, token)).Document)
            .SequenceEqual(JsonDefaults.Serialize(original.Document))).IsTrue();
    }

    private async Task<Dictionary<string, ProcessFaultCommandProtocol.Identity>> ReadIdentitiesAsync(
        IEnumerable<string> resources, CancellationToken token)
    {
        var result = new Dictionary<string, ProcessFaultCommandProtocol.Identity>(StringComparer.Ordinal);
        foreach (var name in resources)
        {
            var observed = await ClusterFixtureApplicationStartup.ReadAsync(fixture.App, name, token);
            result.Add(name, ProcessRuntimeProtocol.Identity(observed));
        }
        return result;
    }

    private async Task RefuseAsync(ProcessFaultCommandProtocol.Identity expected, CancellationToken token)
    {
        var result = await fixture.App.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(
            McpCallerProtocol.Node1, ProcessFaultCommandProtocol.CommandName,
            AspireProcessFaultCommand.Arguments(expected), token);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Data).IsNull();
    }
}
