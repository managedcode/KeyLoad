using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class DueWorkKeyBoundTests
{
    [Test]
    public async Task OversizedReadableKeyFailsThePrefixAndDefersItWithoutInventingACursor()
    {
        using var fixture = new RecurringSagaDatabase();
        var key = KeyCodec.Encode(DueWorkProtocol.ScheduleSpace, RecurringSagaDatabase.TenantId,
            RecurringSagaDatabase.DatabaseId, RecurringSagaDatabase.Domain, PartitionId, fixture.Queue.Queue,
            new string('x', OversizedComponentCharacters));
        await Assert.That(key.Length > DueWorkProtocol.MaximumKeyBytes).IsTrue();
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, [0x01]);
            return true;
        });

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            DueWorkDiscovery.ReadPage(fixture.Database, null, DueWorkTestData.WakeAt));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(DueWorkProtocol.KeyExceedsBound);

        var deferred = DueWorkCursor.DeferNext(fixture.Store.Identity, null);
        await Assert.That(deferred.NextPrefix).IsEqualTo(DueWorkKind.Saga);
        await Assert.That(deferred.Schedules.HasUpperBound).IsFalse();
        await Assert.That(deferred.Sagas.HasUpperBound).IsFalse();
    }

    private const string PartitionId = "partition";
    private const int OversizedComponentCharacters = 4_100;
}
