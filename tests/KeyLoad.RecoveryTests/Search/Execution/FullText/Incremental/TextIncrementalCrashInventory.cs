using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.RecoveryTests.Search;

internal static class TextIncrementalCrashInventory
{
    private const int MaximumPostings = 2;

    internal static async Task<(TextIncrementalRecord[] Records, TextIncrementalPosting[] Postings)>
        ReadAsync(TextIncrementalCrashRuntime runtime, TextIndexMaintenanceRequest request)
    {
        var options = runtime.Options.RuntimeText;
        var budget = new ReadExecutionBudget();
        var leaf = TextIncrementalEnrollmentFiles.Find(runtime.ProjectionRoot, request, budget, options)
            ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        var manifest = TextIncrementalMetadata.ReadManifest(Path.Combine(runtime.ProjectionRoot, leaf),
            options.Value.MaximumDiskBytes, budget);
        if (manifest.Bootstrap)
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        IndexOfTokenRecordPreviousToken<ulong, ulong>? index = null;
        var postings = new List<TextIncrementalPosting>();
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(() =>
            {
                index = RuntimeTextIndex.Open(Path.Combine(runtime.ProjectionRoot, leaf, TextProtocol.RuntimeDirectoryCore),
                    new TextFileStreamProvider(runtime.ProjectionRoot, leaf, request.NodeId, options), options);
                var iterator = index.ZoneTree1.CreateIterator(global::ZoneTree.IteratorType.NoRefresh,
                    contributeToTheBlockCache: false);
                var readerFailures = new List<Exception>();
                try
                {
                    ServerFailureObserver.Observe(() =>
                    {
                        while (iterator.Next())
                        {
                            if (postings.Count >= MaximumPostings)
                            {
                                throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
                            }
                            var key = iterator.CurrentKey;
                            postings.Add(new(key.Token, key.Record, key.PreviousToken));
                        }
                    }, readerFailures);
                }
                finally { ServerFailureObserver.Observe(iterator.Dispose, readerFailures); }
                ServerFailureObserver.ThrowIfAny(readerFailures);
                return Task.CompletedTask;
            }, failures);
        }
        finally
        {
            if (index is not null)
            {
                ServerFailureObserver.Observe(index.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return (manifest.Records, [.. postings.OrderBy(posting => posting.Token)]);
    }
}
