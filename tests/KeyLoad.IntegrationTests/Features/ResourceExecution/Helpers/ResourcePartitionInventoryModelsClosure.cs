using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsClosure
{
    internal static Task<ResourcePartitionInventoryModelsState> InitializeAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
                var context = ResourcePartitionInventoryModelsContext.Create(callers, membership.Source.Count);
                var creations = new List<ResourcePartitionInventoryRf3Creation>();
                var batches = new List<ResourcePartitionInventoryModelsBatch>();
                var beforeWrite = TimeProvider.System.GetUtcNow();
                var steps = ResourcePartitionInventoryModelsProtocol.Steps;
                for (var ordinal = 0; ordinal < steps.Length; ordinal++)
                {
                    batches.Add(await ResourcePartitionInventoryModelsWrites.RunAsync(wave, seed, membership,
                        context, steps[ordinal], ordinal, creations, token));
                }
                var blob = await ResourcePartitionInventoryModelsBlob.PublishAsync(
                    wave, seed, membership, context, creations, token);
                var table = await ResourcePartitionInventoryModelsTable.CreateAsync(wave, seed, membership, context, token);
                var model = await ResourcePartitionInventoryModelsHistory.CaptureAsync(
                    context, batches[ResourcePartitionInventoryModelsProtocol.Index], table, blob, beforeWrite, token);
                await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
                var definitions = creations.Select(creation => creation.Request.Definition).Append(table.Definition).ToArray();
                var inventory = await ResourcePartitionInventoryModelsInventoryAssertions.CaptureAsync(
                    wave, seed, membership, definitions, token);
                var state = new ResourcePartitionInventoryModelsState(creations.ToImmutableArray(),
                    batches.ToImmutableArray(), model, inventory);
                await ResourcePartitionInventoryModelsReplay.VerifyAsync(context, state, token);
                var afterReplay = await ResourcePartitionInventoryModelsInventoryAssertions.CaptureAsync(
                    wave, seed, membership, definitions, token);
                await ResourcePartitionInventoryModelsInventoryAssertions.SameAsync(inventory, afterReplay);
                return state;
            }, token);

    internal static Task VerifyAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        ResourcePartitionInventoryModelsState state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
                var context = ResourcePartitionInventoryModelsContext.Create(callers, membership.Source.Count);
                var definitions = state.Inventory.Select(item => item.Definition).ToArray();
                var before = await ResourcePartitionInventoryModelsInventoryAssertions.CaptureAsync(
                    wave, seed, membership, definitions, token);
                await ResourcePartitionInventoryModelsInventoryAssertions.SameAsync(state.Inventory, before);
                await ResourcePartitionInventoryModelsReplay.VerifyAsync(context, state, token);
                var after = await ResourcePartitionInventoryModelsInventoryAssertions.CaptureAsync(
                    wave, seed, membership, definitions, token);
                await ResourcePartitionInventoryModelsInventoryAssertions.SameAsync(before, after);
                return true;
            }, token);
}
