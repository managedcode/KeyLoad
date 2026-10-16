using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Budget
{
    private const string Resource = "d2-inventory-budget";
    private const string Domain = "d2-inventory-budget-domain";
    private const string KeyPrefix = "budget-";
    private const string Id = "budget-row";
    private const string Json = "{\"private\":\"d2-inventory-private-payload\",\"value\":31}";
    private const int MaximumPartitions = 64;
    private const int FirstExtraPartition = 1;
    private const int OfficialAlternation = 2;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed original,
        RemoteTransferNativeMembership membership, CancellationToken token)
    {
        var creation = new ResourcePartitionInventoryRf3Creation(Guid.NewGuid(),
            ResourcePartitionInventoryRf3Protocol.Configure(ResourcePartitionInventoryRf3Protocol.Definition(Resource, Domain)));
        var writes = new List<ResourcePartitionInventoryRf3Original>();
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, original.Credential,
            async callers =>
            {
                await SqlRf3Protocol.EqualAsync(creation.Request.Definition, await McpCallerAssertions.SdkSuccessAsync(
                    await callers.Sdk.ConfigureResourceAsync(creation.CommandId, creation.Request, token).ConfigureAwait(false)));
                writes.Add(await WriteAsync(callers, 0, token).ConfigureAwait(false));
                return true;
            }, token).ConfigureAwait(false);
        var calibration = await ResourcePartitionInventoryRf3Assertions.SuccessAsync(
            await ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, original, membership, Resource, token).ConfigureAwait(false));
        var template = calibration.Entries.Single();
        var maximum = NativeResourceOwnerInventoryDiagnosticProtocol.MaximumPayloadBytes(Options.Create(new DatabaseLimits()));
        var lowerBound = (long)NativeSerialization.Measure(template);
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, original.Credential,
            async callers =>
            {
                for (var index = FirstExtraPartition; index < MaximumPartitions && lowerBound <= maximum; index++)
                {
                    var write = await WriteAsync(callers, index, token).ConfigureAwait(false);
                    writes.Add(write);
                    var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(
                        new(1, write.Command.Partition), token).ConfigureAwait(false));
                    lowerBound += NativeSerialization.Measure(template with
                    {
                        Partition = write.Command.Partition,
                        CurrentPlacement = placement
                    });
                }
                return true;
            }, token).ConfigureAwait(false);
        await Assert.That(lowerBound).IsGreaterThan((long)maximum);
        await ResourcePartitionInventoryRf3Assertions.RefusalAsync(
            await ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, original, membership, Resource, token).ConfigureAwait(false),
            ErrorCode.BudgetExceeded).ConfigureAwait(false);
        var expanded = original with
        {
            Creations = [creation],
            Originals = writes.ToImmutableArray()
        };
        await ResourcePartitionInventoryRf3PublicAssertions.ReplayAsync(wave, expanded, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3PublicAssertions.ReplayAsync(wave, original, token).ConfigureAwait(false);
        var healthy = await ResourcePartitionInventoryRf3Assertions.SuccessAsync(
            await ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, original, membership,
                ResourcePartitionInventoryRf3Protocol.Unused, token).ConfigureAwait(false));
        await Assert.That(healthy.Entries.IsEmpty).IsTrue();
    }

    private static Task<ResourcePartitionInventoryRf3Original> WriteAsync(RequestCqrsRf3Callers callers,
        int index, CancellationToken token)
        => ResourcePartitionInventoryRf3Corpus.WriteAsync(callers,
            new(ResourcePartitionInventoryRf3Protocol.Tenant, ResourcePartitionInventoryRf3Protocol.Database,
                Domain, KeyPrefix + index.ToString(CultureInfo.InvariantCulture)), Resource, Id, Json,
            official: index % OfficialAlternation != 0, token);
}
