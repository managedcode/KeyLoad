using System.Collections.Immutable;
using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3CurrentWorkload
{
    private const int CurrentCount = 20;
    private const int RetentionDeleteLimit = 3;
    private const int FirstCurrentSequence = 26;
    private const int FirstCurrentMinute = 30;
    private const int CurrentValueBase = 101;
    private const int FloorValue = 25;
    private const int StoppedFollowerValue = 201;
    private const int StoppedFollowerMinute = 60;
    private const string CurrentPrefix = "native6-sample-";
    private const string CurrentTags = "{\"origin\":\"native6\"}";
    private const string ExpiredEventId = "new-expired-id";
    private const string FloorEventId = "at-retention-floor";

    internal static async Task<ImmutableArray<NodeEpochRf3ExpectedSample>> ApplyRetentionAsync(
        DistributedApplication app, NodeEpochRf3Profile profile, NodeEpochRf3Workload workload,
        CancellationToken cancellationToken)
    {
        await using var admin = await NodeEpochRf3Callers.ConnectAsync(app, NodeEpochRf3Protocol.Node1,
            NodeEpochRf3Protocol.Node2, profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await using var reader = await NodeEpochRf3Callers.ConnectAsync(app, NodeEpochRf3Protocol.Node3,
            NodeEpochRf3Protocol.Node1, workload.Reader.Secret, cancellationToken).ConfigureAwait(false);
        var initial = NodeEpochRf3ReadOracle.Prior(workload);
        await NodeEpochRf3ReadOracle.VerifyFullAsync(reader, workload, initial, cancellationToken).ConfigureAwait(false);
        await RetryPriorCommandAsync(admin, workload, cancellationToken).ConfigureAwait(false);
        await ReplaySameEventAsync(admin, reader, workload, cancellationToken).ConfigureAwait(false);
        await RejectChangedEventReplayAsync(admin, reader, workload, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3AuthorizationOracle.RejectReadOnlyExpiryAsync(reader, workload, cancellationToken)
            .ConfigureAwait(false);
        await ExpireAndAppendAtFloorAsync(admin, workload, cancellationToken).ConfigureAwait(false);
        var retained = NodeEpochRf3ReadOracle.RetainedAfterFloor(workload);
        await NodeEpochRf3ReadOracle.VerifyRetentionStatusAsync(admin, workload,
            NodeEpochRf3Protocol.SampleStart.AddMinutes(10), 10, false, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3ReadOracle.VerifyFullAsync(reader, workload, retained, cancellationToken).ConfigureAwait(false);
        return retained;
    }

    internal static async Task<ImmutableArray<NodeEpochRf3ExpectedSample>> AppendCurrentAsync(
        DistributedApplication app, NodeEpochRf3Profile profile, NodeEpochRf3Workload workload,
        ImmutableArray<NodeEpochRf3ExpectedSample> current, CancellationToken cancellationToken)
    {
        await using var callers = await NodeEpochRf3Callers.ConnectAsync(app, NodeEpochRf3Protocol.Node1,
            NodeEpochRf3Protocol.Node2, profile.AdminKey, cancellationToken).ConfigureAwait(false);
        var added = ImmutableArray.CreateBuilder<NodeEpochRf3ExpectedSample>(CurrentCount);
        for (var index = 0; index < CurrentCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = CurrentSample(index);
            var sequence = FirstCurrentSequence + index;
            var command = AppendCommand(workload, data, CurrentTags);
            if (index % 2 == 0)
            { _ = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false); }
            else
            {
                _ = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            }
            added.Add(new(data, sequence, CurrentTags));
        }
        var all = current.AddRange(added.ToImmutable());
        await NodeEpochRf3ReadOracle.VerifyFullAsync(callers, workload, all, cancellationToken).ConfigureAwait(false);
        return all;
    }

    internal static async Task<NodeEpochRf3ExpectedSample> AppendWhileFollowerStoppedAsync(
        NodeEpochRf3Workload workload,
        KeyLoad.Client.KeyLoadClient writer, CancellationToken cancellationToken)
    {
        var sample = new SampleData("native6-after-follower-loss", NodeEpochRf3Protocol.SampleStart
            .AddMinutes(StoppedFollowerMinute), StoppedFollowerValue);
        var command = AppendCommand(workload, sample, CurrentTags);
        var receipt = await RetryRecognizedAmbiguousWriteAsync(writer, command, cancellationToken).ConfigureAwait(false);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        return new(sample, FirstCurrentSequence + CurrentCount, CurrentTags);
    }

    internal static CommandRequest AppendCommand(NodeEpochRf3Workload workload, SampleData sample, string tags)
        => new(Guid.NewGuid(), workload.Partition,
            [new AppendSamples(workload.SeriesSet, workload.SeriesId, [sample], tags)]);

    private static async Task RetryPriorCommandAsync(NodeEpochRf3Callers callers,
        NodeEpochRf3Workload workload, CancellationToken cancellationToken)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(workload.ReplayCommand,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, workload.ReplayCommand, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk.CommandId).IsEqualTo(workload.ReplayReceipt.CommandId);
        await Assert.That(mcp.Value.CommandId).IsEqualTo(workload.ReplayReceipt.CommandId);
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(workload.ReplayReceipt))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(mcp.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(workload.ReplayReceipt))).IsTrue();
        await Assert.That(mcp.Value.Token).IsEqualTo(sdk.Token);
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value))).IsTrue();
    }

    private static async Task ReplaySameEventAsync(NodeEpochRf3Callers admin, NodeEpochRf3Callers reader,
        NodeEpochRf3Workload workload, CancellationToken cancellationToken)
    {
        var original = workload.PriorSamples[10];
        var command = AppendCommand(workload, original, NodeEpochRf3Workload.Tags(10));
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(mcp.Value.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(sdk.Token).IsEqualTo(mcp.Value.Token);
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value))).IsTrue();
        await NodeEpochRf3ReadOracle.VerifySeriesAsync(reader, workload, NodeEpochRf3ReadOracle.Prior(workload),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RejectChangedEventReplayAsync(NodeEpochRf3Callers admin, NodeEpochRf3Callers reader,
        NodeEpochRf3Workload workload, CancellationToken cancellationToken)
    {
        var source = workload.PriorSamples[10];
        var changed = source with { Value = source.Value + 100 };
        var command = AppendCommand(workload, changed, NodeEpochRf3Workload.Tags(10));
        var sdk = await admin.Sdk.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await McpCallerAssertions.ErrorAsync(await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            command, cancellationToken).ConfigureAwait(false), ErrorCode.Conflict, dispatched: true).ConfigureAwait(false);
        await NodeEpochRf3ReadOracle.VerifySeriesAsync(reader, workload, NodeEpochRf3ReadOracle.Prior(workload),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExpireAndAppendAtFloorAsync(NodeEpochRf3Callers admin,
        NodeEpochRf3Workload workload, CancellationToken cancellationToken)
    {
        var cutoff = NodeEpochRf3Protocol.SampleStart.AddMinutes(10);
        await ExpirePagesAsync(admin, workload, cutoff, cancellationToken).ConfigureAwait(false);
        await RejectNewExpiredIdentityAsync(admin, workload, cutoff, cancellationToken).ConfigureAwait(false);
        var atFloor = new SampleData(FloorEventId, cutoff, FloorValue);
        var floorCommand = AppendCommand(workload, atFloor, CurrentTags);
        var native = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, floorCommand, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(floorCommand,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(native.Value.CommandId).IsEqualTo(floorCommand.CommandId);
        await Assert.That(sdk.CommandId).IsEqualTo(floorCommand.CommandId);
        await Assert.That(native.Value.Token).IsEqualTo(sdk.Token);
        await Assert.That(JsonDefaults.Serialize(native.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(sdk))).IsTrue();
    }

    private static async Task ExpirePagesAsync(NodeEpochRf3Callers admin, NodeEpochRf3Workload workload,
        DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var counts = new long[] { 3, 6, 9, 10 };
        for (var page = 0; page < counts.Length; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = new CommandRequest(Guid.NewGuid(), workload.Partition,
                [new ExpireSamples(workload.SeriesSet, workload.SeriesId, cutoff, RetentionDeleteLimit)]);
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);
            var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.That(sdk.CommandId).IsEqualTo(command.CommandId);
            await Assert.That(mcp.Value.CommandId).IsEqualTo(command.CommandId);
            await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value))).IsTrue();
            await VerifyPageStatusAsync(admin, workload, cutoff, counts[page], page < counts.Length - 1,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task VerifyPageStatusAsync(NodeEpochRf3Callers admin, NodeEpochRf3Workload workload,
        DateTimeOffset cutoff, long cumulativeDeletes, bool hasMore, CancellationToken cancellationToken)
        => NodeEpochRf3ReadOracle.VerifyRetentionStatusAsync(admin, workload, cutoff, cumulativeDeletes, hasMore,
            cancellationToken);

    private static async Task RejectNewExpiredIdentityAsync(NodeEpochRf3Callers admin,
        NodeEpochRf3Workload workload, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var sample = new SampleData(ExpiredEventId, cutoff.AddMinutes(-1), 50);
        var command = AppendCommand(workload, sample, CurrentTags);
        var sdk = await admin.Sdk.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await McpCallerAssertions.ErrorAsync(await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            command, cancellationToken).ConfigureAwait(false), ErrorCode.HistoryUnavailable, dispatched: true).ConfigureAwait(false);
    }

    private static async Task<CommitReceipt> RetryRecognizedAmbiguousWriteAsync(KeyLoad.Client.KeyLoadClient writer,
        CommandRequest command, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var result = await writer.CommitAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            { return result.Value!; }
            var code = result.Problem?.ErrorCode;
            if (code is not (nameof(ErrorCode.UnknownWriteOutcome) or nameof(ErrorCode.OwnershipLost)))
            { throw new InvalidOperationException("The RF3 follower-loss write returned an unrecognized result code."); }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("The same-ID RF3 follower-loss command did not reconcile before its bound.");
    }

    private static SampleData CurrentSample(int index) => new(CurrentPrefix
        + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
        NodeEpochRf3Protocol.SampleStart.AddMinutes(FirstCurrentMinute + index), CurrentValueBase + index);

}
