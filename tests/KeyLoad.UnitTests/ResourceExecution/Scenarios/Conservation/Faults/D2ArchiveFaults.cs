using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveFaults
{
    private const string OriginRefusal = "The recovered resource owner inventory origin differs from its exact admitted transformation.";
    private const string ImageRefusal = "The recovered native restore image contains changed, missing or extra canonical bytes.";

    internal static async Task ExecuteAsync(D2ArchivePlan plan, D2ArchiveCapture archive, CancellationToken token)
    {
        var attacks = new[] { D2ArchiveAttack.InvalidOriginVersion, D2ArchiveAttack.ForeignOriginIncarnation,
            D2ArchiveAttack.WrongOriginRevision, D2ArchiveAttack.UnrelatedDocumentBytes };
        foreach (var attack in attacks)
        {
            var slot = plan.Slot(archive, (int)attack, "refused-" + attack, Guid.NewGuid());
            var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(), TimeProvider.System, token);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => D2ArchiveRestoreSlots.Restore(slot, work, token, attack));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo(attack == D2ArchiveAttack.UnrelatedDocumentBytes ? ImageRefusal : OriginRefusal);
            await Assert.That(Directory.Exists(slot.Destination)).IsFalse();
            await D2ArchiveCaptureFlow.UnchangedAsync(archive, token);
        }
    }

    internal static void Alter(IAtomicTransaction transaction, D2ArchiveSource source, D2ArchiveAttack attack)
    {
        if (attack == D2ArchiveAttack.None)
        {
            return;
        }
        if (attack == D2ArchiveAttack.UnrelatedDocumentBytes)
        {
            var key = DocumentStorageKeys.RecordKey(source.Partition, source.UnrelatedLifetime.ResourceName, D2ArchiveData.UnrelatedId);
            var bytes = transaction.ReadOwnedValue(key) ?? throw new InvalidOperationException("The actual unrelated document is missing.");
            bytes[^D2ArchiveData.Single] ^= byte.MaxValue;
            transaction.Put(key, bytes);
            return;
        }
        var originKey = ResourceOwnerInventoryKeys.Origin();
        var original = transaction.GetRecord<ResourceOwnerInventoryOriginV1>(originKey)
            ?? throw new InvalidOperationException("The genuine reconciled origin is missing.");
        var changed = attack switch
        {
            D2ArchiveAttack.InvalidOriginVersion => original with { Version = D2ArchiveData.InvalidOriginVersion },
            D2ArchiveAttack.ForeignOriginIncarnation => original with { StoreIncarnation = Guid.NewGuid() },
            D2ArchiveAttack.WrongOriginRevision => original with { Revision = checked(original.Revision + D2ArchiveData.Single) },
            _ => throw new InvalidOperationException("The bounded archive attack is invalid.")
        };
        transaction.PutRecord(originKey, changed);
    }
}
