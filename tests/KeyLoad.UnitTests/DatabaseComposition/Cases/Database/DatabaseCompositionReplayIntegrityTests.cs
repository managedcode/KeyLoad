using KeyLoad.Core;
using KeyLoad.Core.Features.DatabaseComposition;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionReplayIntegrityTests
{
    [Test]
    public async Task AcComp005MissingAndForeignSavedAuthorityFailClosed()
    {
        using var db = CompositionReplayFixture.NewDatabase();
        db.Commit(new EnqueueMessage(CompositionReplayFixture.Queue, CompositionReplayFixture.Message, CompositionReplayFixture.Link(db, CompositionReplayFixture.First, CompositionReplayFixture.Second)));
        var operation = CompositionReplayFixture.Forward(db);
        db.Database.Apply(operation).Get<CommitReceipt>();
        var key = OutcomeStoreOracle.Key(db.Store, operation);
        var outcome = db.Store.Read(view => view.GetRecord<StoredOutcome>(key))!;
        await Assert.That(outcome.CompositionAuthority).IsNotNull();

        db.Store.Commit((tx, _) => { tx.PutRecord(key, outcome with { CompositionAuthority = null }); return true; });
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Apply(operation).Get<CommitReceipt>());
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.RecoveryRequired);

        var foreign = new PartitionRef(db.Partition.TenantId, db.Partition.DatabaseId,
            db.Partition.TransactionDomainId, CompositionReplayFixture.OtherPartition);
        var invalid = new CompositionOutcomeAuthority([new EntityRef(foreign, CompositionReplayFixture.Collection, CompositionReplayFixture.First)]);
        db.Store.Commit((tx, _) => { tx.PutRecord(key, outcome with { CompositionAuthority = invalid }); return true; });
        var corrupt = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Apply(operation).Get<CommitReceipt>());
        await Assert.That(corrupt.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcComp005PersistedSourceFieldPolicyChangeDeniesReplayWithoutPrincipalEpochChange()
    {
        using var db = CompositionReplayFixture.NewDatabase();
        db.Commit(new EnqueueMessage(CompositionReplayFixture.Queue, CompositionReplayFixture.Message, CompositionReplayFixture.Link(db, CompositionReplayFixture.First, CompositionReplayFixture.Second)));
        CompositionReplayFixture.ConfigureReader(db);
        var operation = CompositionReplayFixture.Forward(db, CompositionReplayFixture.Principal);
        db.Database.Apply(operation).Get<CommitReceipt>();
        var epoch = db.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(CompositionReplayFixture.Principal))!.PolicyEpoch);
        var key = KeySpace.Resource(db.Partition.TenantId, db.Partition.DatabaseId, CompositionReplayFixture.Queue);
        var source = db.Store.Read(view => view.GetRecord<ResourceDefinition>(key))!;
        var protectedSource = source with
        {
            FieldPolicies = [new SensitiveFieldPolicy("/payload", "restricted",
                RawReadGrant: "new.raw.read", RawUseGrant: "new.raw.use")]
        };
        db.Store.Commit((tx, _) => { tx.PutRecord(key, protectedSource); return true; });

        var denied = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.Apply(operation).Get<CommitReceipt>());
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(CompositionReplayFixture.Principal))!.PolicyEpoch))
            .IsEqualTo(epoch);
    }
}
