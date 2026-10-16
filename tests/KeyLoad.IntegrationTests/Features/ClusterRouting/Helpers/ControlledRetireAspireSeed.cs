using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using C = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireCorpus;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspireSeed
{
    internal static async Task<ControlledRetireAspireSeedState> CreateAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        foreach (var (name, kind) in new[] { (C.Collection, ResourceKind.Collection),
            (C.Topic, ResourceKind.Topic), (C.Queue, ResourceKind.WorkQueue) })
        {
            var definition = new ResourceDefinition(name, kind, seed.Partition.TransactionDomainId);
            await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                await seed.Source.ConfigureResourceAsync(Guid.NewGuid(),
                    new(seed.Partition.TenantId, seed.Partition.DatabaseId, definition), token)));
        }
        var command = C.Seed(seed.Partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(C.SeedId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(seed.OriginalPlacement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(seed.OriginalPlacement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(C.Revision);
        await SqlRf3Protocol.EqualAsync(C.Mutations, receipt.Mutations);
        var blob = await ControlledRetireAspireBlob.CreateAsync(seed, token);
        return new(command, receipt, blob);
    }

    internal static async Task<ControlledRetireAspireSeedState> RecordedAtAsync(
        ControlledRetireAspireSeedState state, IReadOnlyDictionary<string, NativeRetireCutWitness> cuts)
    {
        var matching = cuts.Values.Where(cut => cut.Identity.Incarnation == state.Receipt.Token.Incarnation).ToArray();
        await Assert.That(matching.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        ReplicatedOperation? original = null;
        foreach (var cut in matching)
        {
            var entry = cut.ReceiptEntry ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
            await Assert.That(entry.Index).IsEqualTo(state.Receipt.Token.Position);
            await Assert.That(cut.CommittedIndex).IsGreaterThanOrEqualTo(entry.Index);
            var operation = entry.Operation ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
            await Assert.That(operation.Id).IsEqualTo(state.Command.CommandId);
            await Assert.That(operation.Kind).IsEqualTo(OperationKind.Batch);
            await Assert.That(operation.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
            await SqlRf3Protocol.EqualAsync(state.Command, NativeCommandPayload.Read<CommandRequest>(operation));
            if (original is not null)
            {
                await Assert.That(NativeSerialization.Serialize(original).AsSpan().SequenceEqual(
                    NativeSerialization.Serialize(operation))).IsTrue();
            }
            original = operation;
        }
        return state with
        {
            RecordedAt = original?.EvaluatedAt ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing)
        };
    }

    internal static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireSeedState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(state.Command, token)));
        await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        await ControlledRetireAspireModels.RequireAsync(seed, state, token);
        await ControlledRetireAspireBlob.ReplayAsync(seed, state.Blob, token);
        await seed.VerifyAsync(token);
    }
}
