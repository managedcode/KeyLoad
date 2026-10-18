using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.EventStreams;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Verifies real resource command refusal without effects, then original SDK/MCP fault recovery.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AspireContainerFaultCommandTests(ClusterFixture fixture)
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
        var resources = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ContainerResource>()
            .Where(resource => resource.Name is McpCallerProtocol.Node1 or McpCallerProtocol.Node2 or McpCallerProtocol.Node3)
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name, StringComparer.Ordinal);
        var before = new Dictionary<string, ContainerRuntimeInspection>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            before.Add(resource.Key, await ContainerRuntimeDocker.InspectAsync(resource.Value, token));
        }
        var selected = before[McpCallerProtocol.Node1].Id;
        const int First = 0;
        const int Next = 1;
        var stale = (selected[First] == '0' ? "1" : "0") + selected[Next..];
        foreach (var rejectedId in new[] { "invalid-container-id", stale, before[McpCallerProtocol.Node2].Id })
        {
            await RefuseAsync(rejectedId, token);
            foreach (var resource in resources)
            {
                var actual = await ContainerRuntimeDocker.InspectAsync(resource.Value, token);
                await Assert.That(actual.State).IsEqualTo(ContainerRuntimeProtocol.RunningState);
                await Assert.That(actual).IsEqualTo(before[resource.Key]);
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
        await new AggregateReplayClusterLeaderLossTests(fixture)
            .AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay();
        await Assert.That(JsonDefaults.Serialize(ClusterReplicationTestSupport.Success(
            await sdk.GetAsync(scenario.Reference, token)).Document)
            .SequenceEqual(JsonDefaults.Serialize(original.Document))).IsTrue();
    }

    private async Task RefuseAsync(string expectedId, CancellationToken token)
    {
        var result = await fixture.App.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(
            McpCallerProtocol.Node1, ContainerFaultCommandProtocol.CommandName,
            new InteractionInputCollection([new InteractionInput
            {
                Name = ContainerFaultCommandProtocol.ExpectedIdArgument,
                InputType = InputType.Text,
                Value = expectedId
            }]), token);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Data).IsNull();
    }
}
