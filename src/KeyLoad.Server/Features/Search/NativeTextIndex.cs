using ZoneTree.AbstractFileStream;
using ZoneTree.FullTextSearch;
using ZoneTree.FullTextSearch.Index;
using ZoneTree.FullTextSearch.SearchEngines;
using ZoneTree.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIndex
{
    internal static IndexOfTokenRecordPreviousToken<ulong, ulong> Open(string directory,
        IFileStreamProvider fileStreamProvider)
    {
        var options = new AdvancedZoneTreeOptions<ulong, ulong>
        {
            FileStreamProvider = fileStreamProvider,
            FactoryConfigurator1 = factory =>
            {
                factory.Options.MutableSegmentBloomFilterBitsPerItem = 0;
                factory.SetMutableSegmentMaxItemCount(NativeTextProtocol.MutableSegmentMaximumItems)
                    .ConfigureWriteAheadLogOptions(wal => wal.WriteAheadLogMode = WriteAheadLogMode.Sync);
            }
        };
        var index = new IndexOfTokenRecordPreviousToken<ulong, ulong>(directory,
            useSecondaryIndex: false, blockCacheLifeTimeInMilliseconds: 0, advancedOptions: options);
        index.Maintainer1.EnableJobForCleaningInactiveCaches = false;
        return index;
    }

    internal static CompositeKeyOfTokenRecordPrevious<ulong, ulong> LowerBound(ulong token)
        => new() { Token = token, Record = 0, PreviousToken = 0 };

    internal static void ValidatePath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)
            || Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) != directory)
        {
            throw KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Validation, NativeTextProtocol.InvalidProjection);
        }
    }
}
