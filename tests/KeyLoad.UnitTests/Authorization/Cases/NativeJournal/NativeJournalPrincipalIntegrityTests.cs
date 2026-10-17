using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class NativeJournalPrincipalIntegrityTests
{
    private const string RootPrincipalId = "root";
    private const string AppliedCutResourceName = "authorization-cut";
    private static readonly DateTimeOffset OperationTime = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] InvalidPrincipalBytes = [0xFF, 0x00, 0x01];

    [Test]
    public async Task AcAuth002MissingProtectedPrincipalAtExistingCutRequiresRecovery()
    {
        using var database = new TestDatabase();
        NativeJournalAuthorityTestBootstrap.Initialize(database.Database);
        AdvanceAppliedCut(database);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId));
            return true;
        });

        var exception = Assert.ThrowsExactly<KeyLoadException>(() => NativeJournalAuthorityTestBootstrap.Verify(database.Database));
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(ReadPrincipal(database)).IsNull();
    }

    [Test]
    public async Task AcAuth002ModifiedProtectedPrincipalAtExistingCutRequiresRecovery()
    {
        using var database = new TestDatabase();
        NativeJournalAuthorityTestBootstrap.Initialize(database.Database);
        AdvanceAppliedCut(database);
        database.Store.Commit((transaction, _) =>
        {
            var key = KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId);
            var principal = transaction.GetRecord<PrincipalRecord>(key)!;
            transaction.PutRecord(key, principal with
            {
                PolicyEpoch = principal.PolicyEpoch + 1
            });
            return true;
        });

        var exception = Assert.ThrowsExactly<KeyLoadException>(() => NativeJournalAuthorityTestBootstrap.Verify(database.Database));
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(ReadPrincipal(database)!.PolicyEpoch).IsEqualTo(2L);
    }

    [Test]
    public async Task AcAuth002CorruptProtectedPrincipalAtExistingCutRequiresRecovery()
    {
        using var database = new TestDatabase();
        NativeJournalAuthorityTestBootstrap.Initialize(database.Database);
        AdvanceAppliedCut(database);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId), InvalidPrincipalBytes);
            return true;
        });

        var exception = Assert.ThrowsExactly<KeyLoadException>(() => NativeJournalAuthorityTestBootstrap.Verify(database.Database));
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.RecoveryRequired);
    }

    private static PrincipalRecord? ReadPrincipal(TestDatabase database)
        => database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId)));

    private static void AdvanceAppliedCut(TestDatabase database)
    {
        var request = new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId,
            new ResourceDefinition(AppliedCutResourceName, ResourceKind.Collection, database.Partition.TransactionDomainId));
        Apply(database.Database, OperationKind.ConfigureResource, request, RootPrincipalId, 1).Get<ResourceDefinition>();
    }

    private static OperationResult Apply(DatabaseEngine database, OperationKind kind, object payload, string principalId,
        long replicationIndex = 0)
        => database.Apply(new ReplicatedOperation(Guid.NewGuid(), kind, principalId, OperationTime,
            JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options)), replicationIndex);
}
