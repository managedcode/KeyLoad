using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPackingActivationRf3Assertions
{
    private const long InitialRevision = 1;

    internal static async Task SamePhysicalAsync(AtomicPartitionPlacementResolution first,
        AtomicPartitionPlacementResolution second)
    {
        await Assert.That(first.IsFallback).IsFalse();
        await Assert.That(second.IsFallback).IsFalse();
        await Assert.That(first.Partition).IsNotEqualTo(second.Partition);
        await Assert.That(first.Partition.AtomicPartitionId).IsNotEqualTo(second.Partition.AtomicPartitionId);
        await Assert.That(first.Partition.PartitionKey).IsEqualTo(second.Partition.PartitionKey);
        await Assert.That(first.Partition.DatabaseId).IsEqualTo(second.Partition.DatabaseId);
        await Assert.That(first.Partition.TransactionDomainId).IsEqualTo(second.Partition.TransactionDomainId);
        await Assert.That(first.Partition.TenantId).IsNotEqualTo(second.Partition.TenantId);
        await AtomicPartitionPlacementRf3Assertions.SameOwnerAsync(first, second);
        await Assert.That(first.VoterIds.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        await Assert.That(first.VoterIds.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        await Assert.That(first.Revision).IsEqualTo(InitialRevision);
        await Assert.That(second.Revision).IsEqualTo(InitialRevision);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers, CommandRequest command,
        CommitReceipt receipt, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await SqlRf3Protocol.EqualAsync(receipt, sdk);
        await SqlRf3Protocol.EqualAsync(receipt, mcp.Value);
    }

    internal static async Task SecondAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers second,
        TenantPackingActivationRf3State state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.FirstPlacement,
            await TenantPackingActivationRf3Admission.ReadAsync(administrator, state.First.Partition, token));
        await SqlRf3Protocol.EqualAsync(state.SecondPlacement,
            await TenantPackingActivationRf3Admission.ReadAsync(administrator, state.Second.Partition, token));
        await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(second, state.Second,
            RequestCqrsRf3Protocol.DocumentJson, InitialRevision, token);
        await ReplayAsync(second, state.SecondSeed, state.SecondReceipt, token);
    }
}
