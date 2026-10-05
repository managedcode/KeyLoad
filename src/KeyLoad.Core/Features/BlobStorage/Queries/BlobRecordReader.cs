using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobRecordReader(Guid incarnation)
{
    internal static T? Get<T>(IKeyValueView view, byte[] key) where T : class
    {
        T? record = null;
        view.ReadValue(key, value => record = Decode<T>(value));
        return record;
    }

    internal static T Decode<T>(ReadOnlySpan<byte> bytes)
    {
        BlobMetadataRules.BeforeDecode<T>(bytes);
        try
        {
            var record = NativeSerialization.Deserialize<T>(bytes);
            BlobMetadataRules.AfterDecode(record);
            return record;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        { throw BlobErrors.Corruption(); }
    }

    internal BlobHead? Head(IKeyValueView view, BlobRef blob)
    {
        var head = Get<BlobHead>(view, BlobKeys.Head(blob));
        if (head is null)
        { return null; }
        ValidateHead(head, blob, incarnation);
        var quota = BlobQuotaOperations.Read(view, BlobKeys.Quota(blob), incarnation);
        if (quota.ObjectKeys < 1)
        { throw BlobErrors.Corruption(); }
        if (head.Metadata.VersionId is not null)
        { _ = Current(view, head); }
        return head;
    }

    internal BlobState? State(IKeyValueView view, BlobRef blob, Guid upload)
    {
        var state = Get<BlobState>(view, BlobKeys.State(blob, upload));
        if (state is null)
        { return null; }
        ValidateState(state, blob, upload, incarnation);
        var quota = BlobQuotaOperations.Read(view, BlobKeys.Quota(blob), incarnation);
        if (quota.ObjectKeys < 1 || quota.Versions < 1 || quota.ReservedBytes < state.ChargedBytes
            || state.Status == BlobUploadStatus.Active && quota.Uploads < 1)
        { throw BlobErrors.Corruption(); }
        return state;
    }

    internal static void Version(int version)
    {
        if (version != BlobKeys.FormatVersion)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, BlobErrors.Unsupported); }
    }

    internal static void ValidateHead(BlobHead head, BlobRef blob, Guid authority)
        => BlobHeadValidation.Validate(head, blob, authority);

    internal static void ValidateState(BlobState state, BlobRef blob, Guid upload, Guid authority)
        => BlobStateValidation.Validate(state, blob, upload, authority);

    internal static void Hash(string hash)
    {
        try
        { _ = BlobIntegrity.Matches(hash, hash); }
        catch (KeyLoadException) { throw BlobErrors.Corruption(); }
    }

    internal static int PartCount(long length) => checked((int)((length + BlobLimits.RawPartBytes - 1) / BlobLimits.RawPartBytes));

    internal static BlobPartMetadata Part(IKeyValueView view, BlobState state, int ordinal, StorageValueReader reader)
    {
        var meta = Get<BlobPartMetadata>(view, BlobKeys.PartMeta(state.Blob, state.UploadId, ordinal)) ?? throw BlobErrors.Corruption();
        Version(meta.FormatVersion);
        var expected = checked((int)Math.Min(BlobLimits.RawPartBytes, state.DeclaredLength - (long)ordinal * BlobLimits.RawPartBytes));
        if (ordinal < state.ReclaimCursor || ordinal >= state.NextOrdinal || meta.Length != expected || expected < 1)
        { throw BlobErrors.Corruption(); }
        Hash(meta.Sha256);
        var found = view.ReadValue(BlobKeys.Part(state.Blob, state.UploadId, ordinal), bytes =>
        {
            if (bytes.Length != meta.Length || !BlobIntegrity.Matches(BlobIntegrity.PartHash(bytes), meta.Sha256))
            { throw BlobErrors.Corruption(); }
            reader(bytes);
        });
        if (!found)
        { throw BlobErrors.Corruption(); }
        return meta;
    }

    internal BlobState Current(IKeyValueView view, BlobHead head)
    {
        var metadata = head.Metadata;
        var state = State(view, metadata.Blob, metadata.VersionId ?? throw BlobErrors.Corruption()) ?? throw BlobErrors.Corruption();
        if (state.Status != BlobUploadStatus.Complete || state.Retired || state.ReclaimCursor != 0
            || state.ExpectedRevision != metadata.Revision - 1
            || metadata.Length != state.DeclaredLength || metadata.PartCount != state.NextOrdinal
            || metadata.IntegrityHash != state.IntegrityHash || metadata.Access != state.Access)
        { throw BlobErrors.Corruption(); }
        return state;
    }
}
