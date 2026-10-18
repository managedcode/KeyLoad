using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationCredentialColdAssertions
{
    internal static async Task RunAsync(AuthorizationTelemetryHttpFixture fixture,
        (Guid Id, ConfigureApiKeyRequest Request, Result<object> Result) repair,
        CancellationToken cancellationToken)
    {
        var original = AuthorizationTelemetryPolicyRepair.Capture(fixture.Database);
        var identity = fixture.Database.Store.Identity;
        var applied = fixture.Database.Database.LastApplied;
        await fixture.JoinForColdAsync(cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Database.Directory), fixture.Database.StorageExecution,
            UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() => VerifyAsync(fixture, repair,
                original, identity, applied, store, cancellationToken), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyAsync(AuthorizationTelemetryHttpFixture fixture,
        (Guid Id, ConfigureApiKeyRequest Request, Result<object> Result) repair,
        (string[] Bytes, long Position) original, global::KeyLoad.Storage.StoreIdentity identity,
        long applied, ZoneTreeStore store, CancellationToken cancellationToken)
    {
        await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(identity))).IsTrue();
        await ImageAsync(store, original);
        var database = fixture.Database.CreateConfiguredDatabase(store);
        await Assert.That(database.LastApplied).IsEqualTo(applied);
        var replay = database.ApplyEmbedded(database.CreateCommand(OperationKind.ConfigureApiKey, repair.Id, AuthorizationCredentialHttpExportProtocol.RootPrincipal, default, JsonSerializer.Serialize(repair.Request, JsonDefaults.Options)), cancellationToken);
        await Assert.That(NativeSerialization.Serialize(replay).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(repair.Result))).IsTrue();
        await ImageAsync(store, original);
        var principal = database.Authenticate(AuthorizationTelemetryTestProtocol.MemberKey,
            TimeProvider.System.GetUtcNow());
        await Assert.That(principal).IsEqualTo(AuthorizationTelemetryTestProtocol.Member);
        var partition = fixture.Database.Partition;
        var page = new AdminCatalogReader(database).Read(principal,
            new(partition.TenantId, partition.DatabaseId), cancellationToken);
        await AuthorizationTelemetryAssertions.CatalogAsync(page, partition, applied);
        await ImageAsync(store, original);
    }

    private static async Task ImageAsync(ZoneTreeStore store, (string[] Bytes, long Position) original)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(original.Bytes,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(original.Position);
    }
}
