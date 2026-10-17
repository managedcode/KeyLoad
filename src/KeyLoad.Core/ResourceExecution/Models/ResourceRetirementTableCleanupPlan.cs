using KeyLoad.Storage;
using static KeyLoad.Core.Features.ResourceExecution.ResourceRetirementTableCleanupFamilies;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Retains one original bounded visit; shrinking never refunds or repeats observed work.</summary>
internal sealed class ResourceRetirementTableCleanupPlan(
    int familyOrdinal, IReadOnlyList<byte[]> keys, IReadOnlyList<long> valueBytes,
    bool moreInFamily, long examinedBytes, int examinedRecords)
{
    internal int Count => keys.Count;

    internal ResourceRetirementTableCleanupSelection Select(int count)
    {
        if (count < Empty || count > Count || count == Empty && Count != Empty)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        var more = count < Count || moreInFamily;
        var next = familyOrdinal == All.Length ? new ResourceRetirementTableCleanupCursor(All.Length)
            : more ? new ResourceRetirementTableCleanupCursor(familyOrdinal, keys[count - Step])
            : new ResourceRetirementTableCleanupCursor(familyOrdinal + Step);
        var mutations = new StorageMutation[count];
        long deletedBytes = Empty;
        for (var index = Empty; index < count; index++)
        {
            mutations[index] = new(keys[index], null);
            deletedBytes = checked(deletedBytes + keys[index].LongLength + valueBytes[index]);
        }
        var progress = new ResourceRetirementTableCleanupProgress(next, count, examinedBytes,
            deletedBytes, next.FamilyOrdinal < All.Length)
        {
            ExaminedRecords = examinedRecords
        };
        return new(progress, mutations);
    }
}
