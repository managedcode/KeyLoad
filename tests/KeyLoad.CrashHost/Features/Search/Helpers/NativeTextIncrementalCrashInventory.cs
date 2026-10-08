using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextIncrementalCrashInventory
{
    private const int MaximumPostings = 2;

    internal static async Task<(NativeTextIncrementalRecord[] Records, NativeTextIncrementalPosting[] Postings)>
        ReadAsync(NativeTextIncrementalCrashRuntime runtime, TextIndexMaintenanceRequest request)
    {
        var options = runtime.Options.NativeText;
        var budget = new ReadExecutionBudget(CrashExecutionOptions.DatabaseLimits(runtime.Database.Limits));
        var leaf = NativeTextIncrementalEnrollmentFiles.Find(runtime.ProjectionRoot, request, budget, options)
            ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        var manifest = NativeTextIncrementalMetadata.ReadManifest(Path.Combine(runtime.ProjectionRoot, leaf),
            options.Value.MaximumDiskBytes, budget);
        if (manifest.Bootstrap)
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        IndexOfTokenRecordPreviousToken<ulong, ulong>? index = null;
        var postings = new List<NativeTextIncrementalPosting>();
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(() =>
            {
                index = NativeTextIndex.Open(Path.Combine(runtime.ProjectionRoot, leaf, NativeTextProtocol.NativeDirectory),
                    new NativeTextFileStreamProvider(runtime.ProjectionRoot, leaf, request.NodeId, options), options);
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
                            { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
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
            { ServerFailureObserver.Observe(index.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return (manifest.Records, [.. postings.OrderBy(posting => posting.Token)]);
    }
}
