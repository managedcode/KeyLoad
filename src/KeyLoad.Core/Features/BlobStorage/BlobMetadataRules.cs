using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobMetadataRules
{
    internal static void Access(RowAccess? access, bool persisted)
    {
        if (access is null)
        { throw persisted ? BlobErrors.Corruption() : BlobErrors.Validation(); }
        try
        {
            if (access.OwnerId is { } owner)
            { JsonData.Identifier(owner); }
            if (access.ProjectId is { } project)
            { JsonData.Identifier(project); }
        }
        catch (KeyLoadException) when (persisted)
        { throw BlobErrors.Corruption(); }
    }

    internal static void EncodedLength(int length)
    {
        if (length > BlobLimits.MaxMetadataRecordBytes)
        { throw BlobErrors.Corruption(); }
    }

    internal static void BeforeDecode<T>(ReadOnlySpan<byte> bytes)
    {
        if (typeof(T) == typeof(BlobHead) || typeof(T) == typeof(BlobState))
        { EncodedLength(bytes.Length); }
    }

    internal static void AfterDecode<T>(T record)
    {
        if (record is BlobHead head)
        { Access(head.Metadata?.Access, true); }
        if (record is BlobState state)
        { Access(state.Access, true); }
    }

    internal static byte[] Encode<T>(T record)
    {
        try
        {
            using var counter = new ResultByteCounterStream(BlobLimits.MaxMetadataRecordBytes, static () => { });
            JsonSerializer.Serialize(counter, record, JsonDefaults.Options);
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.BudgetExceeded)
        { throw BlobErrors.Validation(); }
        return JsonDefaults.Serialize(record);
    }

    internal static void Put<T>(IAtomicTransaction tx, byte[] key, T record)
        => tx.Put(key, Encode(record));
}
