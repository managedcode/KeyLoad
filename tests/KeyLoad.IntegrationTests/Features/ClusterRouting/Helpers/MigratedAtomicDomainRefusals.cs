using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MigratedAtomicDomainRefusals
{
    private const string OtherDomain = "kl069-other-domain";
    private const string DomainDetail = "The resource belongs to a different transaction domain.";

    internal static async Task<MigratedAtomicDomainState> CreateAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, long directoryRevision, CancellationToken token)
    {
        var original = seed.Originals[0].Command;
        await Assert.That(original.Partition).IsEqualTo(seed.Partition);
        await Assert.That(seed.Partition.TransactionDomainId).IsNotEqualTo(OtherDomain);
        var changed = original with
        {
            Partition = original.Partition with
            {
                TransactionDomainId = OtherDomain
            },
            Mutations = [original.Mutations[0]]
        };
        await Assert.That(changed.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(changed.Partition.TenantId).IsEqualTo(seed.Partition.TenantId);
        await Assert.That(changed.Partition.DatabaseId).IsEqualTo(seed.Partition.DatabaseId);
        await Assert.That(changed.Partition.PartitionKey).IsEqualTo(seed.Partition.PartitionKey);
        await Assert.That(changed.Partition.AtomicPartitionId).IsNotEqualTo(seed.Partition.AtomicPartitionId);
        var fallback = seed.OriginalPlacement with
        {
            Partition = changed.Partition,
            DirectoryRevision = directoryRevision,
            Revision = 0,
            IsFallback = true
        };
        var domain = new MigratedAtomicDomainState(changed, fallback);
        await MigratedAtomicDomainCurrent.RunAsync(seed, atomic, domain, token);
        await ReplayAsync(seed, atomic, domain, token);
        return domain;
    }

    internal static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain, CancellationToken token)
    {
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            var result = await sdk.CommitAsync(domain.Refused, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Value).IsNull();
            await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(result.Problem, ErrorCode.Conflict, DomainDetail);
            await MigratedAtomicDomainCurrent.RunAsync(seed, atomic, domain, token);
        }
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await seed.Official.CallAsync(
            McpCallerTools.DocumentsCommit, domain.Refused, token), ErrorCode.Conflict,
            PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail);
        await MigratedAtomicDomainCurrent.RunAsync(seed, atomic, domain, token);
        await PartitionMovementPublicParentRf3Cut.RequireModelsAsync(seed, token);
        await MigratedAtomicDomainCurrent.RunAsync(seed, atomic, domain, token);
    }
}
