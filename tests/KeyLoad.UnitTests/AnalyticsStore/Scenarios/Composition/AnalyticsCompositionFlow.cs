using KeyLoad.UnitTests.Features.Authorization;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsCompositionOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsCompositionData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsCompositionFlow
{
    internal static Task RunAsync(CancellationToken token) => SafeProjectorOperationDatabase.RunAsync(async owner =>
    {
        await O.ConfigureAsync(owner);
        var anchor = await O.CommitAsync(owner, O.Batch(owner,
            [new PutDocument(D.AnchorCollection, D.AnchorId, D.AnchorJson, 0)], D.Root), [new(P.Put, D.AnchorCollection, D.AnchorId, 1)]);
        var first = await HealthyAsync(owner, P.First, 1);
        AnalyticsCompositionCommit[] current = [first];
        await O.ReplayAsync(owner, first, 1);
        await AnalyticsCompositionModels.RequireAsync(owner, current);
        await AnalyticsCompositionOutbox.RequireAsync(owner, anchor, current);
        await AnalyticsCompositionRefusals.RequireAsync(owner, O.Batch(owner, P.Mutations(owner, P.Rejected, true)),
            P.Rejected, ErrorCode.RevisionConflict, P.CasDetail, current);
        var denied = O.Batch(owner, P.Mutations(owner, P.Denied));
        await O.PolicyAsync(owner, 2, false);
        await AnalyticsCompositionRefusals.RequireAsync(owner, denied, P.Denied,
            ErrorCode.PermissionDenied, P.DeniedDetail, current);
        await O.PolicyAsync(owner, 3, true);
        var second = await HealthyAsync(owner, P.Second, 2);
        current = [first, second];
        await O.ReplayAsync(owner, first);
        await O.ReplayAsync(owner, second);
        await AnalyticsCompositionModels.RequireAsync(owner, current);
        await AnalyticsCompositionOutbox.RequireAsync(owner, anchor, current);
        await AnalyticsCompositionCold.RunAsync(owner, anchor, current);
    }, token);

    internal static Task<AnalyticsCompositionCommit> HealthyAsync(SafeProjectorOperationDatabase owner, string id, long generation)
        => O.CommitAsync(owner, O.Batch(owner, P.Mutations(owner, id)),
            [new(P.Put, D.AnchorCollection, id, 1), new(P.Upsert, D.Store, id, generation),
             new(P.Edge, P.Graph, id, 1), new(P.Append, P.Events, id, 1)], generation == 1 ? 1 : 3);
}
