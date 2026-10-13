using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedHealthy
{
    internal const string DocumentId = "capacity-occupied-owner-healthy";
    private const long InitialDocumentRevision = 0;
    private const long OriginalDocumentRevision = 1;
    private const long InitialPosition = 0;
    private const int RequiredMutationCount = 1;
    internal const string Json = "{\"healthyCapacityOwner\":true}";

    internal static async Task<TenantCapacityPlacementRf3OccupiedHealthyState> CreateAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantDedicatedOwnerRf3State packed, CancellationToken token)
    {
        var primary = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents, DocumentId, Json, ExpectedRevision: InitialDocumentRevision));
        var primaryReceipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(primary, token));
        await ReceiptAsync(primary, primaryReceipt, RelationalSqlRf3Tokens.Documents);
        var sibling = new CommandRequest(Guid.NewGuid(), packed.Sibling.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, DocumentId, Json, ExpectedRevision: InitialDocumentRevision)]);
        var siblingReceipt = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, packed.Sibling.Secret, async caller =>
            {
                var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(sibling, token));
                await ReceiptAsync(sibling, receipt, RequestCqrsRf3Protocol.AdminCollection);
                return receipt;
            }, token);
        var original = new TenantCapacityPlacementRf3OccupiedHealthyState(primary, primaryReceipt, sibling, siblingReceipt);
        await TenantCapacityPlacementRf3OccupiedHealthyAssertions.RequireAsync(wave, seed, packed, original, token);
        return original;
    }

    private static async Task ReceiptAsync(CommandRequest request, CommitReceipt receipt, string collection)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(InitialPosition);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(RequiredMutationCount);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt("putDocument", collection, DocumentId, OriginalDocumentRevision), receipt.Mutations.Single());
    }
}
