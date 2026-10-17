using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MigratedAtomicReadMinimumBoundary
{
    private const int FirstOriginal = 0;
    private const long EmptyPosition = 0;
    private const string DomainDetail = "The resource belongs to a different transaction domain.";
    private const string MissingResourceDetail = "The resource is not configured.";
    private const string TokenScopeDetail = "The document session token belongs to another atomic partition or placement.";

    internal static async Task RunAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain,
        ImmutableArray<MigratedCatalogScopeState> catalogs, CancellationToken token)
    {
        var minimum = seed.Originals[FirstOriginal].Receipt.Token;
        await Assert.That(minimum.Incarnation).IsEqualTo(seed.OriginalPlacement.Incarnation);
        await Assert.That(minimum.OwnershipEpoch).IsEqualTo(seed.OriginalPlacement.PlacementEpoch);
        await Assert.That(minimum.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(minimum.Position).IsGreaterThan(EmptyPosition);
        await Assert.That(domain.Fallback.Incarnation).IsEqualTo(minimum.Incarnation);
        await Assert.That(domain.Fallback.PlacementEpoch).IsEqualTo(minimum.OwnershipEpoch);
        foreach (var scope in Scopes(seed, domain, catalogs))
        {
            await Assert.That(scope.Reference.Partition.AtomicPartitionId).IsNotEqualTo(minimum.AtomicPartitionId);
            await RequireAsync(seed, atomic, domain, catalogs, scope, null, token);
            await RequireAsync(seed, atomic, domain, catalogs, scope, minimum, token);
        }
        await ReplayAsync(seed, atomic, domain, catalogs, token);
    }

    private static ImmutableArray<MigratedAtomicReadScope> Scopes(PartitionMovementPublicParentRf3Seed seed,
        MigratedAtomicDomainState domain, ImmutableArray<MigratedCatalogScopeState> catalogs)
    {
        var scopes = ImmutableArray.CreateBuilder<MigratedAtomicReadScope>();
        scopes.Add(new(seed.Models.First with
        {
            Partition = domain.Refused.Partition
        }, ErrorCode.Conflict, DomainDetail));
        foreach (var catalog in catalogs)
        {
            scopes.Add(new(seed.Models.First with
            {
                Partition = catalog.Refused.Partition
            }, ErrorCode.NotFound, MissingResourceDetail));
        }
        return scopes.ToImmutable();
    }

    private static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain,
        ImmutableArray<MigratedCatalogScopeState> catalogs, MigratedAtomicReadScope scope,
        CommitToken? minimum, CancellationToken token)
    {
        var code = minimum is null ? scope.CatalogCode : ErrorCode.TokenInvalidated;
        var detail = minimum is null ? scope.CatalogDetail : TokenScopeDetail;
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            var originalRead = minimum is null
                ? sdk.GetAsync(scope.Reference, token)
                : sdk.GetAsync(scope.Reference, minimum, token);
            var refusal = await originalRead;
            await Assert.That(refusal.IsSuccess).IsFalse();
            await Assert.That(refusal.Value).IsNull();
            await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(refusal.Problem, code, detail);
            await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, catalogs, token);
        }
        var safeDetail = minimum is null ? PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail : TokenScopeDetail;
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await seed.Official.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scope.Reference, minimum), token), code, safeDetail);
        await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, catalogs, token);
    }

    private static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain,
        ImmutableArray<MigratedCatalogScopeState> catalogs, CancellationToken token)
    {
        var original = seed.Originals[FirstOriginal];
        await SqlRf3Protocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.CommitAsync(original.Command, token)));
        await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, catalogs, token);
        await SqlRf3Protocol.EqualAsync(original.Receipt,
            (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await seed.Official.CallAsync(
                McpCallerTools.DocumentsCommit, original.Command, token))).Value);
        await MigratedCatalogScopeCurrent.RunAsync(seed, atomic, domain, catalogs, token);
    }
}
