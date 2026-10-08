using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveInstalledImageValidation
{
    internal static void Require(IKeyValueView view, PartitionMoveTargetStage stage,
        Guid destination, DatabaseLimits limits)
    {
        var ordinal = PartitionMoveProtocol.EmptyCount;
        long examined = PartitionMoveProtocol.EmptyCount;
        foreach (var family in stage.Descriptor.Families)
        {
            if (PartitionMoveFamilyCapture.ControlOwned(family.Family))
            { continue; }
            VerifyFamily(view, stage, family, destination, limits, ref ordinal, ref examined);
        }
        if (ordinal != stage.AcceptedPages || ordinal != stage.InstalledPages)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
    }

    private static void VerifyFamily(IKeyValueView view, PartitionMoveTargetStage stage,
        PartitionMoveImageFamily family, Guid destination, DatabaseLimits limits,
        ref int ordinal, ref long examined)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[]? after = null;
        long records = PartitionMoveProtocol.EmptyCount;
        long bytes = PartitionMoveProtocol.EmptyCount;
        for (var index = PartitionMoveProtocol.EmptyCount; index < family.PageCount; index++)
        {
            var page = PartitionMoveTargetStorage.Read<PartitionMoveImagePage>(view,
                PartitionMoveTargetStorage.PageKey(stage.Control.Partition, stage.Control.MoveId, ordinal),
                limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage);
            PartitionMovePageAdmission.Require(page, stage.Fence, limits, limits.MaxBatchBytes);
            if (page.Ordinal != ordinal || page.Family != family.Family)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
            VerifyPage(view, stage, page, destination, limits, hash, ref after, ref examined);
            records = checked(records + page.Records.Length);
            bytes = checked(bytes + page.Records.Sum(value => (long)value.Key.Length + value.Value.Length));
            ordinal = checked(ordinal + PartitionMoveProtocol.SequenceStep);
        }
        if (records != family.RecordCount || bytes != family.RawBytes
            || Convert.ToHexStringLower(hash.GetHashAndReset()) != family.Digest
            || view.Scan(KeySpace.Partition(family.Family, stage.Control.Partition),
                PartitionMoveProtocol.SequenceStep, after).Records.Length != PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
    }

    private static void VerifyPage(IKeyValueView view, PartitionMoveTargetStage stage,
        PartitionMoveImagePage page, Guid destination, DatabaseLimits limits, IncrementalHash hash,
        ref byte[]? after, ref long examined)
    {
        var actual = view.Scan(KeySpace.Partition(page.Family, stage.Control.Partition), page.Records.Length, after);
        if (actual.Records.Length != page.Records.Length)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
        for (var index = PartitionMoveProtocol.EmptyCount; index < page.Records.Length; index++)
        {
            var original = page.Records[index];
            var target = actual.Records[index];
            examined = checked(examined + original.Key.Length + original.Value.Length
                + target.Key.Length + target.Value.Length);
            if (examined > limits.MaxQueryReadBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
            var expected = PartitionMoveBlobImport.Value(page, original,
                stage.Fence.SourcePlacement.Incarnation, destination);
            if (!original.Key.Span.SequenceEqual(target.Key.Span) || !expected.AsSpan().SequenceEqual(target.Value.Span))
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
            PartitionMoveImageDigest.Append(hash, original.Key.Span);
            PartitionMoveImageDigest.Append(hash, original.Value.Span);
        }
        after = page.Records[^PartitionMoveProtocol.SequenceStep].Key.ToArray();
    }
}
