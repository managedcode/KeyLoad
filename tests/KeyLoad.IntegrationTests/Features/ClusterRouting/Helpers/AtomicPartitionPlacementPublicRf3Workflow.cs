using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class AtomicPartitionPlacementPublicRf3Workflow(ClusterFixture fixture)
{
    private const int RequestVersion = 1;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var scenario = AtomicPartitionPlacementRf3ScenarioFactory.Create();
        var boundRequest = new AtomicPartitionPlacementReadRequest(RequestVersion, scenario.Partition);
        var fallback = await AtomicPartitionPlacementRf3Assertions.SdkReadAsync(fixture.App,
            RequestCqrsRf3Protocol.Node1, fixture.AdminKey, boundRequest, cancellationToken).ConfigureAwait(false);
        await Assert.That(fallback.IsFallback).IsTrue();
        await Assert.That(fallback.DirectoryRevision).IsEqualTo(0L);
        await Assert.That(fallback.Revision).IsEqualTo(0L);
        await AtomicPartitionPlacementRf3Assertions.ValidOwnerAsync(fallback, fixture.PhysicalShardId);
        await using (var initialMcp = await McpOfficialClient.ConnectAsync(fixture.App,
                         RequestCqrsRf3Protocol.Node1, fixture.AdminKey, cancellationToken).ConfigureAwait(false))
        {
            var mcpFallback = await AtomicPartitionPlacementRf3Assertions.McpReadAsync(initialMcp, boundRequest,
                cancellationToken).ConfigureAwait(false);
            await AtomicPartitionPlacementRf3Assertions.SameWitnessAsync(fallback, mcpFallback).ConfigureAwait(false);
        }

        var commandId = Guid.NewGuid();
        var bind = Bind(0, scenario.Partition, fixture.PhysicalShardId);
        await BindSdkAsync(commandId, bind, cancellationToken).ConfigureAwait(false);
        var explicitWitness = await AtomicPartitionPlacementRf3Assertions.SdkReadAsync(fixture.App,
            RequestCqrsRf3Protocol.Node1, fixture.AdminKey, boundRequest, cancellationToken).ConfigureAwait(false);
        await VerifyMcpExplicitAsync(scenario.Partition, explicitWitness, cancellationToken).ConfigureAwait(false);
        await VerifyFallbackRevisionAsync(scenario.FallbackPartition, explicitWitness, 1, cancellationToken)
            .ConfigureAwait(false);
        await VerifyReplayAndConflictsAsync(commandId, bind, scenario.Partition, cancellationToken).ConfigureAwait(false);
        await VerifyPersistedAdminDenialAsync(scenario.Partition, cancellationToken).ConfigureAwait(false);
        await VerifyAllVoterReopensAsync(scenario.Partition, explicitWitness, cancellationToken).ConfigureAwait(false);
    }

    private async Task BindSdkAsync(Guid commandId, BindAtomicPartitionPlacementRequest request,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture.App, RequestCqrsRf3Protocol.Node1);
        var result = await new KeyLoadClient(http, fixture.AdminKey)
            .BindAtomicPartitionPlacementAsync(commandId, request, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsTrue();
    }

    private async Task VerifyMcpExplicitAsync(PartitionRef partition,
        AtomicPartitionPlacementResolution expected, CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture.App, RequestCqrsRf3Protocol.Node2,
            fixture.AdminKey, cancellationToken).ConfigureAwait(false);
        var actual = await AtomicPartitionPlacementRf3Assertions.McpReadAsync(mcp,
            new(RequestVersion, partition), cancellationToken).ConfigureAwait(false);
        await Assert.That(actual.IsFallback).IsFalse();
        await Assert.That(actual.DirectoryRevision).IsEqualTo(1L);
        await Assert.That(actual.Revision).IsEqualTo(1L);
        await AtomicPartitionPlacementRf3Assertions.ValidOwnerAsync(actual, fixture.PhysicalShardId);
        await AtomicPartitionPlacementRf3Assertions.SameWitnessAsync(expected, actual).ConfigureAwait(false);
    }

    private async Task VerifyFallbackRevisionAsync(PartitionRef partition,
        AtomicPartitionPlacementResolution expected, long directoryRevision, CancellationToken cancellationToken)
    {
        var request = new AtomicPartitionPlacementReadRequest(RequestVersion, partition);
        var result = await AtomicPartitionPlacementRf3Assertions.SdkReadAsync(fixture.App,
            RequestCqrsRf3Protocol.Node1, fixture.AdminKey, request, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsFallback).IsTrue();
        await Assert.That(result.DirectoryRevision).IsEqualTo(directoryRevision);
        await Assert.That(result.Revision).IsEqualTo(0L);
        await Assert.That(result.Partition).IsEqualTo(partition);
        await AtomicPartitionPlacementRf3Assertions.SameOwnerAsync(expected, result).ConfigureAwait(false);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture.App,
            RequestCqrsRf3Protocol.Node2, fixture.AdminKey, cancellationToken).ConfigureAwait(false);
        var mcpResult = await AtomicPartitionPlacementRf3Assertions.McpReadAsync(mcp, request, cancellationToken)
            .ConfigureAwait(false);
        await AtomicPartitionPlacementRf3Assertions.SameWitnessAsync(result, mcpResult).ConfigureAwait(false);
    }

    private async Task VerifyReplayAndConflictsAsync(Guid commandId, BindAtomicPartitionPlacementRequest request,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        await BindMcpAsync(commandId, request, cancellationToken).ConfigureAwait(false);
        var changed = request with { PhysicalShardId = Guid.NewGuid() };
        await BindMcpErrorAsync(commandId, changed, ErrorCode.Conflict, cancellationToken).ConfigureAwait(false);
        await BindMcpErrorAsync(Guid.NewGuid(), request with { ExpectedRevision = 0,
            Partition = AtomicPartitionPlacementRf3ScenarioFactory.Create().FallbackPartition },
            ErrorCode.Conflict, cancellationToken).ConfigureAwait(false);
        await BindMcpErrorAsync(Guid.NewGuid(), request with { ExpectedRevision = 1,
            PhysicalShardId = Guid.NewGuid() }, ErrorCode.UnsupportedCapability, cancellationToken).ConfigureAwait(false);
        var after = await AtomicPartitionPlacementRf3Assertions.SdkReadAsync(fixture.App,
            RequestCqrsRf3Protocol.Node1, fixture.AdminKey, new(RequestVersion, partition), cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(after.IsFallback).IsFalse();
        await Assert.That(after.DirectoryRevision).IsEqualTo(1L);
        await Assert.That(after.Revision).IsEqualTo(1L);
    }

    private async Task VerifyPersistedAdminDenialAsync(PartitionRef partition, CancellationToken cancellationToken)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Capability.None, cancellationToken)
            .ConfigureAwait(false);
        using var http = McpCallerHttp.Create(fixture.App, RequestCqrsRf3Protocol.Node3);
        var sdk = new KeyLoadClient(http, identity.Secret);
        var read = await sdk.ReadAtomicPartitionPlacementAsync(new(RequestVersion, partition), cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(read.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var deniedBind = await sdk.BindAtomicPartitionPlacementAsync(Guid.NewGuid(),
            Bind(1, partition, fixture.PhysicalShardId), cancellationToken).ConfigureAwait(false);
        await Assert.That(deniedBind.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture.App, RequestCqrsRf3Protocol.Node3,
            identity.Secret, cancellationToken).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(AtomicPartitionPlacementPublicRf3Protocol.ReadTool,
            new AtomicPartitionPlacementReadRequest(RequestVersion, partition), cancellationToken).ConfigureAwait(false),
            ErrorCode.PermissionDenied, dispatched: true).ConfigureAwait(false);
        await BindMcpErrorAsync(Guid.NewGuid(), Bind(1, partition, fixture.PhysicalShardId),
            ErrorCode.PermissionDenied, cancellationToken, mcp).ConfigureAwait(false);
    }

    private async Task VerifyAllVoterReopensAsync(PartitionRef partition,
        AtomicPartitionPlacementResolution expected, CancellationToken cancellationToken)
    {
        foreach (var node in AtomicPartitionPlacementPublicRf3Protocol.Nodes)
        {
            await fixture.KillContainerAsync(node, AtomicPartitionPlacementPublicRf3Protocol.RestartScenario,
                cancellationToken).ConfigureAwait(false);
            await fixture.RestartContainerAsync(node, cancellationToken).ConfigureAwait(false);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var node in AtomicPartitionPlacementPublicRf3Protocol.Nodes)
        { await VerifyVoterAsync(node, partition, expected, cancellationToken).ConfigureAwait(false); }
    }

    private async Task VerifyVoterAsync(string node, PartitionRef partition,
        AtomicPartitionPlacementResolution expected, CancellationToken cancellationToken)
    {
        var request = new AtomicPartitionPlacementReadRequest(RequestVersion, partition);
        var sdk = await AtomicPartitionPlacementRf3Assertions.SdkReadAsync(fixture.App, node, fixture.AdminKey,
            request, cancellationToken).ConfigureAwait(false);
        await Assert.That(sdk.IsFallback).IsFalse();
        await Assert.That(sdk.DirectoryRevision).IsEqualTo(1L);
        await Assert.That(sdk.Revision).IsEqualTo(1L);
        await AtomicPartitionPlacementRf3Assertions.SameWitnessAsync(expected, sdk).ConfigureAwait(false);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture.App, node, fixture.AdminKey,
            cancellationToken).ConfigureAwait(false);
        var mcpValue = await AtomicPartitionPlacementRf3Assertions.McpReadAsync(mcp, request, cancellationToken)
            .ConfigureAwait(false);
        await AtomicPartitionPlacementRf3Assertions.SameWitnessAsync(sdk, mcpValue).ConfigureAwait(false);
        await AtomicPartitionPlacementRf3Assertions.SameWitnessAsync(expected, mcpValue).ConfigureAwait(false);
        await AtomicPartitionPlacementRf3Assertions.ValidOwnerAsync(sdk, fixture.PhysicalShardId);
    }

    private async Task BindMcpAsync(Guid commandId, BindAtomicPartitionPlacementRequest request,
        CancellationToken cancellationToken)
    {
        await using var client = await McpOfficialClient.ConnectAsync(fixture.App, RequestCqrsRf3Protocol.Node1,
            fixture.AdminKey, cancellationToken).ConfigureAwait(false);
        var reply = await client.Client.CallToolAsync(AtomicPartitionPlacementPublicRf3Protocol.BindTool,
            McpBindArguments(commandId, request), cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = await McpCallerAssertions.SuccessAsync<bool>(reply).ConfigureAwait(false);
        await Assert.That(result.Value).IsTrue();
    }

    private async Task BindMcpErrorAsync(Guid commandId, BindAtomicPartitionPlacementRequest request,
        ErrorCode error, CancellationToken cancellationToken, McpOfficialClient? connected = null)
    {
        if (connected is not null)
        { await BindMcpErrorOnAsync(connected, commandId, request, error, cancellationToken).ConfigureAwait(false); return; }
        await using var client = await McpOfficialClient.ConnectAsync(fixture.App, RequestCqrsRf3Protocol.Node1,
            fixture.AdminKey, cancellationToken).ConfigureAwait(false);
        await BindMcpErrorOnAsync(client, commandId, request, error, cancellationToken).ConfigureAwait(false);
    }

    private static async Task BindMcpErrorOnAsync(McpOfficialClient client, Guid commandId,
        BindAtomicPartitionPlacementRequest request, ErrorCode error, CancellationToken cancellationToken)
    {
        var reply = await client.Client.CallToolAsync(AtomicPartitionPlacementPublicRf3Protocol.BindTool,
            McpBindArguments(commandId, request), cancellationToken: cancellationToken).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(reply, error, dispatched: true).ConfigureAwait(false);
    }

    private static Dictionary<string, object?> McpBindArguments(Guid commandId,
        BindAtomicPartitionPlacementRequest request)
        => new(StringComparer.Ordinal)
        {
            [AtomicPartitionPlacementPublicRf3Protocol.CommandHeader] = commandId.ToString(),
            [McpCallerProtocol.Request] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options)
        };

    private static BindAtomicPartitionPlacementRequest Bind(long revision, PartitionRef partition, Guid shardId)
        => new(RequestVersion, revision, partition, shardId);
}
