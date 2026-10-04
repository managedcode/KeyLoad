using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Messaging;

public sealed class DueWorkIdentityTests
{
    [Test]
    public async Task CommandIdentityIsStableForTheFullCanonicalOccurrenceScope()
    {
        var partition = new PartitionRef("tenant", "database", "domain", "partition-a");
        var lane = new QueueLaneRef(partition, "jobs");
        var hint = new DueWorkHint(DueWorkKind.Schedule, lane, ScheduleId, "creator", 1, 7, 9,
            RecurringSagaDatabase.Epoch);

        var command = DueWorkCommandIdentity.Create(hint);
        await Assert.That(command).IsNotEqualTo(Guid.Empty);
        await Assert.That(DueWorkCommandIdentity.Create(hint)).IsEqualTo(command);
        await Assert.That(DueWorkCommandIdentity.Create(hint with { Ordinal = 10 })).IsNotEqualTo(command);
        await Assert.That(DueWorkCommandIdentity.Create(hint with { Generation = 8 })).IsNotEqualTo(command);
        await Assert.That(DueWorkCommandIdentity.Create(hint with
        {
            Lane = new(new(partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, "partition-b"), "jobs")
        })).IsNotEqualTo(command);
    }

    private static readonly Guid ScheduleId = Guid.ParseExact("00112233445566778899aabbccddeeff", "N");
}
