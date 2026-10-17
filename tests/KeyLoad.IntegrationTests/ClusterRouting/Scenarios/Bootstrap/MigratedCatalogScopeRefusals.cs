using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MigratedCatalogScopeRefusals
{
    private const int FirstOriginal = 0;
    private const int FirstMutation = 0;
    private const string OtherScopeSuffix = "-kl069-other";
    private const string MissingResourceDetail = "The resource is not configured.";

    internal static async Task RunAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain, CancellationToken token)
    {
        var scopes = Create(seed, domain);
        await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, scopes, token);
        foreach (var scope in scopes)
        {
            foreach (var sdk in new[] { seed.Source, seed.Target })
            {
                var result = await sdk.CommitAsync(scope.Refused, token);
                await Assert.That(result.IsSuccess).IsFalse();
                await Assert.That(result.Value).IsNull();
                await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(result.Problem,
                    ErrorCode.NotFound, MissingResourceDetail);
                await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, scopes, token);
            }
            await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await seed.Official.CallAsync(
                McpCallerTools.DocumentsCommit, scope.Refused, token), ErrorCode.NotFound,
                PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail);
            await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, scopes, token);
            await ReplayAsync(seed, atomic, domain, scopes, token);
        }
        await MigratedAtomicReadMinimumBoundary.RunAsync(seed, atomic, domain, scopes, token);
    }

    private static ImmutableArray<MigratedCatalogScopeState> Create(PartitionMovementPublicParentRf3Seed seed,
        MigratedAtomicDomainState domain)
    {
        var original = seed.Originals[FirstOriginal].Command;
        var tenant = original.Partition with
        {
            TenantId = original.Partition.TenantId + OtherScopeSuffix
        };
        var database = original.Partition with
        {
            DatabaseId = original.Partition.DatabaseId + OtherScopeSuffix
        };
        return [Scope(original, tenant, domain), Scope(original, database, domain)];
    }

    private static MigratedCatalogScopeState Scope(CommandRequest original, PartitionRef partition,
        MigratedAtomicDomainState domain)
        => new(original with
        {
            Partition = partition,
            Mutations = [original.Mutations[FirstMutation]]
        }, domain.Fallback with
        {
            Partition = partition
        });

    private static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain,
        ImmutableArray<MigratedCatalogScopeState> scopes, CancellationToken token)
    {
        var original = seed.Originals[FirstOriginal];
        await SqlRf3Protocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.CommitAsync(original.Command, token)));
        await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, scopes, token);
        await SqlRf3Protocol.EqualAsync(original.Receipt,
            (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await seed.Official.CallAsync(
                McpCallerTools.DocumentsCommit, original.Command, token))).Value);
        await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, scopes, token);
    }
}
