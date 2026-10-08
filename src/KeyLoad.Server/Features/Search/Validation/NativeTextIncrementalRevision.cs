using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalRevision
{
    private const long InitialRevision = 1;
    private const long RevisionStep = 1;

    internal static void Require(OutboxEntry entry, NativeTextIncrementalRecord? current,
        PartitionRef partition, string collection, ReadExecutionBudget budget)
    {
        budget.Check();
        var after = entry.After ?? throw NativeTextErrors.Corrupt();
        if (after.Reference.Partition != partition || after.Reference.Collection != collection
            || entry.Receipt.Resource != collection || entry.Receipt.Id != after.Reference.Id
            || entry.Receipt.Revision != after.Revision)
        { throw NativeTextErrors.Corrupt(); }
        if (entry.Before is not { } before)
        {
            if (current is not null || after.Revision != InitialRevision || entry.Mutation is not PutDocument)
            { throw NativeTextErrors.Corrupt(); }
        }
        else if (current is null || current.Reference != before.Reference
            || after.Reference != before.Reference || current.Revision != before.Revision
            || current.Deleted != before.Deleted || before.Revision == long.MaxValue
            || after.Revision != before.Revision + RevisionStep
            || current.CanonicalSha256 is null || current.CanonicalSha256.Length != SHA256.HashSizeInBytes
            || !CryptographicOperations.FixedTimeEquals(current.CanonicalSha256, Digest(before, budget)))
        { throw NativeTextErrors.Corrupt(); }
        if (entry.Mutation switch
        {
            PutDocument put => entry.Receipt.Kind != MutationDiscriminatorNames.PutDocument || after.Deleted || put.Id != after.Reference.Id || put.Collection != collection,
            PatchDocument patch => entry.Receipt.Kind != MutationDiscriminatorNames.PatchDocument || after.Deleted || patch.Id != after.Reference.Id || patch.Collection != collection,
            DeleteDocument delete => entry.Receipt.Kind != MutationDiscriminatorNames.DeleteDocument || !after.Deleted || delete.Id != after.Reference.Id || delete.Collection != collection,
            _ => true
        })
        { throw NativeTextErrors.Corrupt(); }
    }

    internal static byte[] Digest(DocumentRecord document, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(NativeSerialization.Measure(document));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        var bytes = NativeSerialization.Serialize(document);
        budget.Check();
        return SHA256.HashData(bytes);
    }
}
