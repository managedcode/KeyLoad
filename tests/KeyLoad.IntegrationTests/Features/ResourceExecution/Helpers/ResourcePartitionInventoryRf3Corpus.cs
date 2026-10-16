using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Corpus
{
    internal static async Task<ResourcePartitionInventoryRf3Seed> CreateAsync(
        TwoRf3MembershipWave wave, CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        var credential = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, token).ConfigureAwait(false);
        return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            credential, callers => CreateAsync(callers, credential, token), token).ConfigureAwait(false);
    }

    private static async Task<ResourcePartitionInventoryRf3Seed> CreateAsync(
        RequestCqrsRf3Callers callers, string credential, CancellationToken token)
    {
        var first = Creation(ResourcePartitionInventoryRf3Protocol.First, ResourcePartitionInventoryRf3Protocol.Domain);
        var second = Creation(ResourcePartitionInventoryRf3Protocol.Second, ResourcePartitionInventoryRf3Protocol.Domain);
        await ConfigureAsync(callers, first, official: false, token).ConfigureAwait(false);
        await ConfigureAsync(callers, second, official: true, token).ConfigureAwait(false);
        ImmutableArray<ResourcePartitionInventoryRf3Original> originals =
        [
            await CommitAsync(callers, ResourcePartitionInventoryRf3Protocol.FirstPartitionKey, first.Request.Definition.Name, "a-zero", "{\"private\":\"d2-inventory-private-payload\",\"value\":10}", false, token),
            await CommitAsync(callers, ResourcePartitionInventoryRf3Protocol.SecondPartitionKey, first.Request.Definition.Name, "a-one", "{\"private\":\"d2-inventory-private-payload\",\"value\":11}", true, token),
            await CommitAsync(callers, ResourcePartitionInventoryRf3Protocol.SecondPartitionKey, second.Request.Definition.Name, "b-one", "{\"private\":\"d2-inventory-private-payload\",\"value\":21}", false, token),
            await CommitAsync(callers, ResourcePartitionInventoryRf3Protocol.ThirdPartitionKey, second.Request.Definition.Name, "b-two", "{\"private\":\"d2-inventory-private-payload\",\"value\":22}", true, token)
        ];
        var unused = Creation(ResourcePartitionInventoryRf3Protocol.Unused, ResourcePartitionInventoryRf3Protocol.OtherDomain);
        await ConfigureAsync(callers, unused, official: true, token).ConfigureAwait(false);
        var existing = originals.Single(original => original.Document.Reference.Id == "b-one");
        var failed = new CommandRequest(Guid.NewGuid(), existing.Command.Partition,
            [new PutDocument(ResourcePartitionInventoryRf3Protocol.First, ResourcePartitionInventoryRf3Protocol.LateId,
                ResourcePartitionInventoryRf3Protocol.LateJson),
             new PutDocument(ResourcePartitionInventoryRf3Protocol.Second, existing.Document.Reference.Id,
                ResourcePartitionInventoryRf3Protocol.LateJson, ResourcePartitionInventoryRf3Protocol.MissingRevision)],
            existing.Command.OwnershipEpoch);
        return new(credential, [first, second, unused], originals, failed);
    }

    private static ResourcePartitionInventoryRf3Creation Creation(string name, string domain)
        => new(Guid.NewGuid(), ResourcePartitionInventoryRf3Protocol.Configure(
            ResourcePartitionInventoryRf3Protocol.Definition(name, domain)));

    private static async Task ConfigureAsync(RequestCqrsRf3Callers callers,
        ResourcePartitionInventoryRf3Creation creation, bool official, CancellationToken token)
    {
        var actual = official
            ? await ResourcePartitionInventoryRf3Metadata.ConfigureAsync(callers, creation, token).ConfigureAwait(false)
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(
                creation.CommandId, creation.Request, token));
        await SqlRf3Protocol.EqualAsync(creation.Request.Definition, actual);
    }

    private static async Task<ResourcePartitionInventoryRf3Original> CommitAsync(RequestCqrsRf3Callers callers,
        string key, string resource, string id, string json, bool official, CancellationToken token)
    {
        return await WriteAsync(callers, ResourcePartitionInventoryRf3Protocol.Partition(key),
            resource, id, json, official, token).ConfigureAwait(false);
    }

    internal static async Task<ResourcePartitionInventoryRf3Original> WriteAsync(RequestCqrsRf3Callers callers,
        PartitionRef partition, string resource, string id, string json, bool official, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk
            .ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(resource, id, json)],
            placement.PlacementEpoch);
        var receipt = official
            ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(command.CommandId,
            new(placement.Incarnation, partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [new(ResourcePartitionInventoryRf3Protocol.PutKind, resource, id, ResourcePartitionInventoryRf3Protocol.InitialRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, receipt);
        return new(command, receipt, new(new(partition, resource, id),
            ResourcePartitionInventoryRf3Protocol.InitialRevision, json, false, []));
    }
}
