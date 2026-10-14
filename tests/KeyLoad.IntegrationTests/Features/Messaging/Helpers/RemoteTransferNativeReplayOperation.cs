using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeReplayOperation
{
    internal static async Task<(NativeCanonicalModelCutRequest Scope, RemoteModelNativeRequest Request,
        RemoteTransferPostAwaitNativeCut Committed)> ExecuteAsync(TwoRf3MembershipWave wave,
        RemoteTransferNativeCallers calls, RemoteTransferColdSeed seed, CommandRequest accept,
        RemoteTransferNativeReplayHold hold, RemoteModelNativeReplayVariation mode, CancellationToken token)
    {
        if (mode is not (RemoteModelNativeReplayVariation.Original2 or RemoteModelNativeReplayVariation.AllWireFaults6))
        {
            throw Missing();
        }
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        var scope = Scope(wave, seed, accept, hold.ArmId);
        var before = await ReadAsync(wave, membership, scope, token);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = calls.Sdk.CommitAsync(accept, operation.Token);
        var failures = new List<Exception>();
        RemoteModelNativeRequest? captured = null;
        RemoteTransferPostAwaitNativeCut? committed = null;
        CommitReceipt? foregroundReceipt = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var witnesses = await hold.WaitAsync(original, membership.Source,
                mode == RemoteModelNativeReplayVariation.Original2 ? 2 : 6, token);
            captured = RequestCqrsNativeReplayInventory.Original(witnesses[0]);
            RemoteTransferNativeGenerationAssertions.Require(captured, membership.Source, membership.Destination);
            var after = await ReadAsync(wave, membership, scope, token);
            if (mode == RemoteModelNativeReplayVariation.Original2)
            {
                committed = RemoteTransferNativeCommittedCut.Read(after[TwoRf3MembershipProtocol.Nodes[3]], seed, accept, captured);
                await RemoteTransferNativeReplayPairAssertions.RequireOriginalRepliesAsync(witnesses, accept,
                    membership.Source, membership.Destination, membership.SourceSecret, membership.DestinationSecret, committed, token);
                RequireSourceUnchanged(before, after, token);
                await RemoteTransferNativeCutLifecycle.RequireTargetAsync(after, seed, accept, captured, committed);
            }
            else
            {
                await RequireWireFaultsAsync(witnesses, accept, membership, token);
                RemoteTransferNativeCutLifecycle.RequireUnchanged(before, after, token);
            }
            hold.Release();
            if (mode == RemoteModelNativeReplayVariation.Original2)
            {
                await QueueProducerRf3Assertions.DeniedAsync(await original, ErrorCode.UnknownWriteOutcome);
            }
            else
            {
                foregroundReceipt = await McpCallerAssertions.SdkSuccessAsync(await original);
            }
            var released = await ReadAsync(wave, membership, scope, token);
            if (mode == RemoteModelNativeReplayVariation.Original2)
            {
                RemoteTransferNativeCutLifecycle.RequireUnchanged(after, released, token);
            }
            committed ??= RemoteTransferNativeCommittedCut.Read(released[TwoRf3MembershipProtocol.Nodes[3]], seed, accept, captured);
            await RemoteTransferNativeCutLifecycle.RequireTargetAsync(released, seed, accept, captured, committed);
            if (foregroundReceipt is { } actualReceipt)
            {
                await SqlRf3Protocol.EqualAsync(committed.OriginalReceipt, actualReceipt);
            }
            await RemoteTransferColdAssertions.ReceiptLiteralAsync(committed.OriginalReceipt, accept,
                seed.Scenario.DestinationQueue, seed.TransferId, RemoteTransferColdProtocol.AcceptKind, committed.OriginalReceipt.Token.Position);
        }, failures);
        await JoinAsync(wave, membership, hold, original, operation, failures);
        if (failures.Count != 0)
        {
            wave.RetainRoots();
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var result = committed ?? throw Missing();
        await RequireReconciledAsync(wave, calls, membership, scope, accept, result.OriginalReceipt, token);
        return (scope, captured ?? throw Missing(), result);
    }

    private static async Task RequireReconciledAsync(TwoRf3MembershipWave wave, RemoteTransferNativeCallers calls,
        RemoteTransferNativeMembership membership, NativeCanonicalModelCutRequest scope, CommandRequest accept,
        CommitReceipt receipt, CancellationToken token)
    {
        var before = await ReadAsync(wave, membership, scope, token);
        await calls.ReplayAsync(accept, receipt, token);
        var after = await ReadAsync(wave, membership, scope, token);
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Skip(3))
        {
            NativeCanonicalModelCutAssertions.RequireUnchanged(before[node], after[node], token);
        }
    }

    private static async Task RequireWireFaultsAsync(IReadOnlyList<RequestCqrsNativeReplayWitness> witnesses,
        CommandRequest accept, RemoteTransferNativeMembership membership, CancellationToken token)
    {
        RemoteModelNativeReplayVariation[] modes = [RemoteModelNativeReplayVariation.MissingQueueTransfer,
            RemoteModelNativeReplayVariation.ReceiptStage, RemoteModelNativeReplayVariation.MissingIntentToken,
            RemoteModelNativeReplayVariation.EmptyNonce, RemoteModelNativeReplayVariation.EmptyCommandId,
            RemoteModelNativeReplayVariation.EmptyDestinationIncarnation];
        if (witnesses.Count != modes.Length)
        {
            throw Missing();
        }
        var nonces = new HashSet<Guid>();
        for (var index = 0; index < modes.Length; index++)
        {
            var actual = await RemoteTransferNativeReplayAssertions.RequireAsync(witnesses[index], modes[index], index + 1,
                accept, membership.Source, membership.Destination, membership.SourceSecret, membership.DestinationSecret, token);
            if (!nonces.Add(actual.Request.Nonce)
                || !witnesses[0].OriginalNativeRequest.AsSpan().SequenceEqual(witnesses[index].OriginalNativeRequest)
                || witnesses[0].RequestId != witnesses[index].RequestId
                || witnesses[0].ConnectionId != witnesses[index].ConnectionId)
            {
                throw Missing();
            }
        }
    }

    private static async Task JoinAsync(TwoRf3MembershipWave wave, RemoteTransferNativeMembership membership,
        RemoteTransferNativeReplayHold hold, Task original, CancellationTokenSource operation, List<Exception> failures)
    {
        ServerFailureObserver.Observe(hold.Release, failures);
        if (failures.Count != 0)
        {
            ServerFailureObserver.Observe(operation.Cancel, failures);
        }
        await ServerFailureObserver.ObserveAsync(async () => { await original; }, failures);
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.WaitForSettlementAsync(
            hold.ArmId, membership.Source, cleanup.Token), failures);
    }

    private static void RequireSourceUnchanged(IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> before,
        IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> after, CancellationToken token)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Take(3))
        {
            NativeCanonicalModelCutAssertions.RequireUnchanged(before[node], after[node], token);
        }
    }

    private static NativeCanonicalModelCutRequest Scope(TwoRf3MembershipWave wave, RemoteTransferColdSeed seed,
        CommandRequest accept, Guid arm)
        => new(Guid.NewGuid(), arm, wave.QueryControls.SessionId, seed.Scenario.SourcePartition,
            seed.Scenario.DestinationPartition, accept.CommandId, seed.Identity.Principal.Id,
            RemoteTransferDistinctProtocol.TechnicalSubject, seed.Scenario.SourceQueue, seed.TransferId, seed.Scenario.DestinationQueue);

    private static Task<IReadOnlyDictionary<string, NativeCanonicalModelCutWitness>> ReadAsync(TwoRf3MembershipWave wave,
        RemoteTransferNativeMembership membership, NativeCanonicalModelCutRequest scope, CancellationToken token)
        => NativeCanonicalModelCutClient.ReadSixAsync(wave, wave.Profile.AdminKey, scope, membership.Owners, membership.Secrets, token);

    private static InvalidOperationException Missing() => new(RemoteTransferDistinctProtocol.Missing);
}
