using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Changes
{
    private const long FirstSequence = 1;
    private const long SecondSequence = 2;
    private const long PublicSequence = 3;
    private const long HealthySequence = 4;
    private const string MissingReceipt = "The original public safe projector mutation receipt is absent.";
    private const string MissingPage = "The actual public safe projector feed is absent.";

    internal static async Task<ChangeFeedPage> WaveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool raw, bool mixed, CancellationToken token, bool rootPayload = false)
    {
        var request = new ReadChangeFeedRequest(history.State.Partition, history.State.Collection);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadChangesAsync(request, token));
            await RememberAsync(actual, history);
            await PageAsync(actual, history, true, mixed, rootPayload);
        }, token);
        ChangeFeedPage? sdk = null;
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(request, token));
            await PageAsync(sdk, history, raw, mixed, rootPayload);
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.ChangesRead, request, token);
            await PageAsync((await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(reply)).Value, history, raw, mixed, rootPayload);
            if (!raw)
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationRf3Protocol.Canary);
            }
        }, token);
        return sdk ?? throw new InvalidOperationException(MissingPage);
    }

    private static async Task RememberAsync(ChangeFeedPage actual, SafeProjectorOperationRf3History history)
    {
        var expectedCount = history.HealthyPatch is not null ? HealthySequence : history.PublicPatch is not null ? PublicSequence : SecondSequence;
        await Assert.That(actual.Changes.Length).IsEqualTo(checked((int)expectedCount));
        foreach (var row in actual.Changes)
        {
            await Assert.That(row.CommittedAt).IsGreaterThan(DateTimeOffset.UnixEpoch);
            if (!history.CommitTimes.TryGetValue(row.Sequence, out var original))
            {
                history.CommitTimes.Add(row.Sequence, row.CommittedAt);
                original = row.CommittedAt;
            }
            await Assert.That(row.CommittedAt).IsEqualTo(original);
        }
        await Assert.That(history.CommitTimes[FirstSequence]).IsEqualTo(history.CommitTimes[SecondSequence]);
    }

    private static async Task PageAsync(ChangeFeedPage actual, SafeProjectorOperationRf3History history, bool raw, bool mixed, bool rootPayload)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationRf3Protocol.PositivePublicPosition);
        var expected = Expected(history, raw, mixed, rootPayload);
        var tail = expected.Last().Sequence;
        await Assert.That(string.IsNullOrWhiteSpace(actual.Cursor)).IsFalse();
        await SqlRf3Protocol.EqualAsync(new ChangeFeedPage(expected, actual.Cursor, tail, tail, FirstSequence, false, actual.CutPosition), actual);
    }

    private static ImmutableArray<DocumentChange> Expected(SafeProjectorOperationRf3History history, bool raw, bool mixed, bool rootPayload)
    {
        var state = history.State;
        var initial = Document(state.First, SafeProjectorOperationRf3Protocol.InitialRevision,
            raw ? SafeProjectorOperationRf3Protocol.InitialJson : SafeProjectorOperationRf3Protocol.InitialSafe, raw, mixed, rootPayload);
        var rows = ImmutableArray.CreateBuilder<DocumentChange>();
        rows.Add(new(FirstSequence, state.SeedReceipt.Token, history.CommitTimes[FirstSequence], state.First,
            SafeProjectorOperationRf3Protocol.InitialRevision, false, null, initial));
        rows.Add(new(SecondSequence, state.SeedReceipt.Token, history.CommitTimes[SecondSequence], state.Second,
            SafeProjectorOperationRf3Protocol.InitialRevision, false, null,
            Document(state.Second, SafeProjectorOperationRf3Protocol.InitialRevision, SafeProjectorOperationRf3Protocol.SecondJson, raw, mixed, rootPayload)));
        var updated = Document(state.First, SafeProjectorOperationRf3Protocol.UpdatedRevision,
            raw ? SafeProjectorOperationRf3Protocol.UpdatedJson : SafeProjectorOperationRf3Protocol.UpdatedSafe, raw, mixed, rootPayload);
        if (history.PublicPatch is not null)
        {
            var receipt = history.PublicReceipt ?? throw new InvalidOperationException(MissingReceipt);
            rows.Add(new(PublicSequence, receipt.Token, history.CommitTimes[PublicSequence], state.First,
                SafeProjectorOperationRf3Protocol.UpdatedRevision, false, initial, updated));
        }
        if (history.HealthyPatch is not null)
        {
            var receipt = history.HealthyReceipt ?? throw new InvalidOperationException(MissingReceipt);
            rows.Add(new(HealthySequence, receipt.Token, history.CommitTimes[HealthySequence], state.First,
                SafeProjectorOperationRf3Protocol.HealthyRevision, false, updated,
                Document(state.First, SafeProjectorOperationRf3Protocol.HealthyRevision,
                    raw ? SafeProjectorOperationRf3Protocol.HealthyJson : SafeProjectorOperationRf3Protocol.HealthySafe, raw, mixed, rootPayload)));
        }
        return rows.ToImmutable();
    }

    private static DocumentResult Document(EntityRef reference, long revision, string json, bool raw, bool mixed, bool rootPayload)
        => new(reference, revision, rootPayload && !raw ? "{}" : json, !raw, raw ? [] : mixed ? [SafeProjectorOperationRf3Protocol.NestedPath] : []);
}
