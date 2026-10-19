using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextBm25EligibilityPages
{
    internal static async Task RequireAsync(Bm25OriginalPhaseRecoveryFlow flow, bool expectedHead,
        bool mutated, bool redacted)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        var result = await ReadAsync(flow, expectedHead);
        var first = TextBm25ExpectedRows.Document(flow.Owner.Source.Partition, "first", mutated ? 2L : 1L,
            redacted ? "{}" : mutated ? "{\"text\":\"needle needle needle\"}" : "{\"text\":\"needle\"}",
            redacted, redacted ? ["/text"] : []);
        var second = TextBm25ExpectedRows.Document(flow.Owner.Source.Partition, "second", 1L,
            redacted ? "{}" : "{\"text\":\"needle needle\"}", redacted, redacted ? ["/text"] : []);
        first = first with { Reference = flow.Owner.Source.ExpectedReference(first.Reference) };
        second = second with { Reference = flow.Owner.Source.ExpectedReference(second.Reference) };
        RankedDocument[] expected = mutated
            ? [new(first, 1d / 61d, null), new(second, 1d / 62d, null)]
            : [new(second, 1d / 61d, null), new(first, 1d / 62d, null)];
        await TextBm25ExpectedRows.RequireOwnershipAsync(expected, result);
        await TextBm25ExpectedRows.RequireWholeValuesAsync(expected, result);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
    }

    internal static async Task DeniedAsync(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => ReadAsync(flow, null));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
    }

    private static async Task<RankedDocument[]> ReadAsync(Bm25OriginalPhaseRecoveryFlow flow, bool? expectedHead)
    {
        var owner = flow.Owner;
        var work = owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        var execution = UnitExecutionOptions.QueryExecution().Value;
        var request = new SearchRequest(owner.Source.Partition, "orders", "/text", "needle");
        RuntimeTextProjection? projection = null;
        ICapturedTextRead? captured = null;
        RankedDocument[]? result = null;
        DistributedTextStatistics? current = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            projection = new(Path.Combine(owner.Source.Directory, "bm25-eligibility-text"),
                UnitExecutionOptions.DatabaseLimits(owner.Database.Limits), owner.Store.Identity.NodeId,
                UnitTextOptions.Execution());
            captured = owner.Database.WithQueryView(Bm25OriginalPhaseRecoveryFlow.Subject,
                request.Partition, request.Collection, (view, principal, resource) =>
                {
                    if (expectedHead is not null)
                    {
                        current = owner.Database.ReadBm25CorpusCurrent(view, principal, resource, request.Partition,
                            request.Collection, request.TextField!, TextProjectionProtocol.TokenizerVersion, ["needle"], work);
                    }
                    return TextCapturedReadFactory.Capture(owner.Database, projection, view, principal, resource, request, work);
                });
            if (expectedHead is { } admitted)
            {
                await Assert.That(current is not null).IsEqualTo(admitted);
            }
            var ranker = new TextRanker(request.Text!, request.TextField!, work, execution.TextBudgetCheckInterval,
                execution.MaximumDocumentWords, execution.MaximumWordCharacters);
            result = SearchCapturedExecution.Complete(owner.Database, new(captured, ranker, null), request,
                work, null, FilteredSearchEligibility.Create(null, work));
        }, failures);
        ServerFailureObserver.Observe(() => captured?.Dispose(), failures);
        ServerFailureObserver.Observe(() => projection?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }
}
