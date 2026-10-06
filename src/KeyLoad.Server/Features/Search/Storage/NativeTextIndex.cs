using Microsoft.Extensions.Options;
using ZoneTree.AbstractFileStream;
using ZoneTree.FullTextSearch;
using ZoneTree.FullTextSearch.Index;
using ZoneTree.FullTextSearch.SearchEngines;
using ZoneTree.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIndex
{
    private const int LowerBoundRecordEmptyCount = 0;
    private const int LowerBoundPreviousTokenEmptyCount = 0;

    internal static IndexOfTokenRecordPreviousToken<ulong, ulong> Open(string directory, IFileStreamProvider fileStreamProvider, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int DisabledBloomFilterBits = 0;
        const int DisabledBlockCacheLifetime = 0;

        var options = new AdvancedZoneTreeOptions<ulong, ulong>
        {
            FileStreamProvider = fileStreamProvider,
            FactoryConfigurator1 = factory =>
            {
                factory.Options.MutableSegmentBloomFilterBitsPerItem = DisabledBloomFilterBits;
                factory.SetMutableSegmentMaxItemCount(executionOptions.Value.MutableSegmentMaximumItems)
                    .ConfigureWriteAheadLogOptions(wal => wal.WriteAheadLogMode = WriteAheadLogMode.Sync);
            }
        };
        var index = new IndexOfTokenRecordPreviousToken<ulong, ulong>(directory,
            useSecondaryIndex: false, blockCacheLifeTimeInMilliseconds: DisabledBlockCacheLifetime, advancedOptions: options);
        index.Maintainer1.EnableJobForCleaningInactiveCaches = false;
        return index;
    }

    internal static CompositeKeyOfTokenRecordPrevious<ulong, ulong> LowerBound(ulong token)
        => new() { Token = token, Record = LowerBoundRecordEmptyCount, PreviousToken = LowerBoundPreviousTokenEmptyCount };

    internal static void ValidatePath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)
            || Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) != directory)
        {
            throw KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Validation, NativeTextProtocol.InvalidProjection);
        }
    }
}
