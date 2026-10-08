using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextIncrementalPostingOracle
{
    private const int ExpectedPostings = 2;
    private const ulong NoPreviousToken = 0;

    internal static async Task VerifyAsync(string root, string leaf, Guid nodeId, ulong record,
        string[] literalTerms, IOptions<NativeTextExecutionOptions> options)
    {
        IndexOfTokenRecordPreviousToken<ulong, ulong>? index = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                index = NativeTextIndex.Open(Path.Combine(root, leaf, NativeTextProtocol.NativeDirectory),
                    new NativeTextFileStreamProvider(root, leaf, nodeId, options), options);
                var observed = new List<(ulong Token, ulong Record, ulong Previous)>();
                var iterator = index.ZoneTree1.CreateIterator(global::ZoneTree.IteratorType.NoRefresh,
                    contributeToTheBlockCache: false);
                var readFailures = new List<Exception>();
                try
                {
                    ServerFailureObserver.Observe(() =>
                    {
                        while (iterator.Next())
                        {
                            if (observed.Count >= ExpectedPostings)
                            { throw new InvalidOperationException(); }
                            var key = iterator.CurrentKey;
                            observed.Add((key.Token, key.Record, key.PreviousToken));
                        }
                    }, readFailures);
                }
                finally { ServerFailureObserver.Observe(iterator.Dispose, readFailures); }
                ServerFailureObserver.ThrowIfAny(readFailures);
                var first = NativeTextHash.Sha256(literalTerms[0]);
                var second = NativeTextHash.Sha256(literalTerms[1]);
                (ulong Token, ulong Record, ulong Previous)[] expected =
                    [(first, record, NoPreviousToken), (second, record, first)];
                await Assert.That(observed.OrderBy(item => item.Token)).IsEquivalentTo(
                    expected.OrderBy(item => item.Token), CollectionOrdering.Matching);
            }, failures);
        }
        finally
        {
            if (index is not null)
            { ServerFailureObserver.Observe(index.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
