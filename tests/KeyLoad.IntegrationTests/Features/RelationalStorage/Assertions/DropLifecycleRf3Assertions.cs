using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropLifecycleRf3Assertions
{
    private const long InitialPosition = 0;
    private const long InitialEpoch = 0;

    internal static DropTableResult Expected(DropTableRequest request, DropLifecycleRf3ReceiptProof proof)
        => new(request.TenantId, request.DatabaseId, request.ResourceName,
            new CommitReceipt(proof.FinalizationCommandId, proof.Token, [], DurabilityProfile.QuorumProcessDurable));

    internal static async Task ResultAsync(DropTableRequest request, DropLifecycleRf3ReceiptProof proof,
        DropTableResult actual)
    {
        await Assert.That(proof.FinalizationCommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(proof.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(string.IsNullOrWhiteSpace(proof.Token.AtomicPartitionId)).IsFalse();
        await Assert.That(proof.Token.Position).IsGreaterThan(InitialPosition);
        await Assert.That(proof.Token.OwnershipEpoch).IsGreaterThan(InitialEpoch);
        var expected = Expected(request, proof);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await NativeSubmissionColdAssertions.ReceiptAsync(actual.Receipt, expected.Receipt);
    }
}
