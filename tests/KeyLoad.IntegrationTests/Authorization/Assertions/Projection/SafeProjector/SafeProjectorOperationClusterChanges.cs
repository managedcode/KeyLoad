using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterChanges
{
    private const long FirstSequence = 1;
    private const long SecondSequence = 2;
    private const long PublicSequence = 3;
    private const long HealthySequence = 4;
    private const string MissingReceipt = "The original public safe projector mutation receipt is absent.";
    private const string MissingPage = "The actual public safe projector feed is absent.";

    internal static async Task<ChangeFeedPage> WaveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool raw, CancellationToken token)
    {
        var request = new ReadChangeFeedRequest(history.State.Partition, history.State.Collection);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadChangesAsync(request, token));
            await RememberAsync(actual, history);
            await PageAsync(actual, history, true);
        }, token);
        ChangeFeedPage? sdk = null;
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(request, token));
            await PageAsync(sdk, history, raw);
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.ChangesRead, request, token);
            await PageAsync((await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(reply)).Value, history, raw);
            if (!raw)
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationClusterProtocol.Canary);
            }
        }, token);
        return sdk ?? throw new InvalidOperationException(MissingPage);
    }

    private static async Task RememberAsync(ChangeFeedPage actual, SafeProjectorOperationClusterHistory history)
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

    private static async Task PageAsync(ChangeFeedPage actual, SafeProjectorOperationClusterHistory history, bool raw)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationClusterProtocol.PositivePublicPosition);
        var expected = Expected(history, raw);
        var tail = expected.Last().Sequence;
        await Assert.That(string.IsNullOrWhiteSpace(actual.Cursor)).IsFalse();
        await SqlClusterProtocol.EqualAsync(new ChangeFeedPage(expected, actual.Cursor, tail, tail, FirstSequence, false, actual.CutPosition), actual);
    }

    private static ImmutableArray<DocumentChange> Expected(SafeProjectorOperationClusterHistory history, bool raw)
    {
        var state = history.State;
        var initial = Document(state.First, SafeProjectorOperationClusterProtocol.InitialRevision,
            raw ? SafeProjectorOperationClusterProtocol.InitialJson : SafeProjectorOperationClusterProtocol.InitialSafe, raw);
        var rows = ImmutableArray.CreateBuilder<DocumentChange>();
        rows.Add(new(FirstSequence, state.SeedReceipt.Token, history.CommitTimes[FirstSequence], state.First,
            SafeProjectorOperationClusterProtocol.InitialRevision, false, null, initial));
        rows.Add(new(SecondSequence, state.SeedReceipt.Token, history.CommitTimes[SecondSequence], state.Second,
            SafeProjectorOperationClusterProtocol.InitialRevision, false, null,
            Document(state.Second, SafeProjectorOperationClusterProtocol.InitialRevision, SafeProjectorOperationClusterProtocol.SecondJson, raw)));
        var updated = Document(state.First, SafeProjectorOperationClusterProtocol.UpdatedRevision,
            raw ? SafeProjectorOperationClusterProtocol.UpdatedJson : SafeProjectorOperationClusterProtocol.UpdatedSafe, raw);
        if (history.PublicPatch is not null)
        {
            var receipt = history.PublicReceipt ?? throw new InvalidOperationException(MissingReceipt);
            rows.Add(new(PublicSequence, receipt.Token, history.CommitTimes[PublicSequence], state.First,
                SafeProjectorOperationClusterProtocol.UpdatedRevision, false, initial, updated));
        }
        if (history.HealthyPatch is not null)
        {
            var receipt = history.HealthyReceipt ?? throw new InvalidOperationException(MissingReceipt);
            rows.Add(new(HealthySequence, receipt.Token, history.CommitTimes[HealthySequence], state.First,
                SafeProjectorOperationClusterProtocol.HealthyRevision, false, updated,
                Document(state.First, SafeProjectorOperationClusterProtocol.HealthyRevision,
                    raw ? SafeProjectorOperationClusterProtocol.HealthyJson : SafeProjectorOperationClusterProtocol.HealthySafe, raw)));
        }
        return rows.ToImmutable();
    }

    private static DocumentResult Document(EntityRef reference, long revision, string json, bool raw)
        => new(reference, revision, json, !raw, raw ? [] : SafeProjectorOperationClusterProtocol.RedactedPaths);
}
