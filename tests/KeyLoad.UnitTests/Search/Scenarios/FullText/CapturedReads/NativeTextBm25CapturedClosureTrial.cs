using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextBm25CapturedClosureTrial
{
    private const string SourceCollection = "orders";

    internal static async Task RunAsync(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var owner = flow.Owner;
        var lifetimeKey = ResourceLifetimeCatalog.Key(owner.Source.Partition.TenantId,
            owner.Source.Partition.DatabaseId, SourceCollection);
        var lifetime = owner.Store.Read(view => view.ReadOwnedValue(lifetimeKey));
        await Assert.That(lifetime).IsNotNull();
        var absent = await ReadAsync(flow);
        var original = flow.Capture(null);
        var receipt = flow.Publish(original);
        await flow.RequireHealthyAsync(original, receipt);
        var entry = NativeSerialization.Serialize(owner.Entry(original.CommandId));
        var present = await ReadAsync(flow);
        await Assert.That(NativeSerialization.Serialize(present).SequenceEqual(
            NativeSerialization.Serialize(absent))).IsTrue();
        var baseline = Bm25OriginalPhaseRecoveryState.Read(owner);
        await Assert.That(NativeSerialization.Serialize(flow.Publish(flow.Reissue(original))).SequenceEqual(
            NativeSerialization.Serialize(receipt))).IsTrue();
        await baseline.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(owner, original, entry);
        owner.Reopen();
        var cold = await ReadAsync(flow);
        await Assert.That(NativeSerialization.Serialize(cold).SequenceEqual(
            NativeSerialization.Serialize(absent))).IsTrue();
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(owner, original, entry);
        var retainedLifetime = owner.Store.Read(view => view.ReadOwnedValue(lifetimeKey));
        await Assert.That(retainedLifetime).IsNotNull();
        await Assert.That(retainedLifetime!.SequenceEqual(lifetime!)).IsTrue();
    }

    private static async Task<RankedDocument[]> ReadAsync(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var owner = flow.Owner;
        var before = Bm25OriginalPhaseRecoveryState.Read(owner);
        var work = owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        var execution = UnitExecutionOptions.QueryExecution().Value;
        var request = new SearchRequest(owner.Source.Partition, SourceCollection, "/text", "needle");
        NativeTextProjection? projection = null;
        ICapturedTextRead? captured = null;
        RankedDocument[]? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            projection = new(Path.Combine(owner.Source.Directory, "bm25-captured-text"),
                UnitExecutionOptions.DatabaseLimits(owner.Database.Limits), owner.Store.Identity.NodeId,
                UnitNativeTextOptions.Execution());
            captured = owner.Database.WithQueryView(Bm25OriginalPhaseRecoveryFlow.Subject,
                request.Partition, request.Collection, (view, principal, resource) =>
                    NativeTextCapturedReadFactory.Capture(owner.Database, projection, view,
                        principal, resource, request, work));
            var ranker = new TextRanker(request.Text!, request.TextField!, work, execution.TextBudgetCheckInterval,
                execution.MaximumDocumentWords, execution.MaximumWordCharacters);
            result = SearchCapturedExecution.Complete(owner.Database, new(captured, ranker, null), request,
                work, null, FilteredSearchEligibility.Create(null, work));
            var document = NativeTextBm25ExpectedRows.Document(owner.Source.Partition,
                "first", 1L, "{\"text\":\"needle\"}", false, []);
            document = document with { Reference = owner.Source.ExpectedReference(document.Reference) };
            RankedDocument[] expected = [new(document, 1d / 61d, null)];
            await NativeTextBm25ExpectedRows.RequireOwnershipAsync(expected, result);
            await NativeTextBm25ExpectedRows.RequireWholeValuesAsync(expected, result);
        }, failures);
        ServerFailureObserver.Observe(() => captured?.Dispose(), failures);
        ServerFailureObserver.Observe(() => projection?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        return result!;
    }
}
