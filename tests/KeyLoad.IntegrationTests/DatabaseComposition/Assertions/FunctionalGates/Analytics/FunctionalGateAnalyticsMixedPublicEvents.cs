using KeyLoad.IntegrationTests.Features.ClientApi;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateAnalyticsMixedPublicProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsMixedPublicEvents
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateAnalyticsMixedPublicState state,
        bool present, long minimumCut, CancellationToken token)
    {
        var request = new ReadStreamRequest(state.Stream, Limit: P.PageLimit);
        EventRecord? retained = null;
        foreach (var page in await FunctionalGateAnalyticsMixedPublicCalls.ReadAsync(context, McpCallerTools.StreamsRead,
            request, context.Reader.Sdk.ReadStreamAsync(request, token), token))
        {
            await Assert.That(page.Stream).IsEqualTo(state.Stream);
            await Assert.That(page.Head).IsEqualTo(new StreamHead(present ? P.Created : P.Empty, P.Created, P.Created));
            await Assert.That(page.Events.Length).IsEqualTo(present ? P.Created : P.Empty);
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimumCut);
            await Assert.That(page.CutPosition).IsGreaterThan(P.Empty);
            await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
            if (!present)
            {
                continue;
            }

            var row = page.Events.Single();
            await Assert.That(row.Stream).IsEqualTo(state.Stream);
            await Assert.That(row.Revision).IsEqualTo((long)P.Created);
            await Assert.That(row.EventSequence).IsGreaterThan(P.Empty);
            await Assert.That(row.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
            await FunctionalGateUserAnalyticsAssertions.EqualAsync(row.Data, state.Appended);
            // Execution sequence/time are assigned by commit; preserve them exactly across all transports.
            retained ??= new(state.Stream, P.Created, row.EventSequence, state.Appended, row.RecordedAt);
            await FunctionalGateUserAnalyticsAssertions.EqualAsync(row, retained);
        }
    }
}
