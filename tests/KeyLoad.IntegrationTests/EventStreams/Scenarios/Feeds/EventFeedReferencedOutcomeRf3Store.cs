using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedReferencedOutcomeRf3Store
{
    private const long OneCommit = 1;
    private const int MissingNativeSuffixBytes = 1;
    private const string Missing = "The fixture original referenced event vector outcome is absent.";

    internal static async Task RunAsync(TwoRf3MembershipWave wave, string node,
        EventFeedControlRequest request, EventFeedControlResult original, CancellationToken token)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                store = new(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
                var principal = PartitionMovementPublicParentRf3Administrator.PrincipalId;
                var outcomeKey = KeySpace.PartitionOutcome(request.ControlPartition, principal, original.Receipt.CommandId);
                var locatorKey = CommandOutcomePartitionLocatorSerialization.ScopedKey(
                    request.ControlPartition, principal, original.Receipt.CommandId);
                await FaultAsync(store, request, outcomeKey, null, token).ConfigureAwait(false);
                var originalOutcome = store.Read(view => view.ReadOwnedValue(outcomeKey))
                    ?? throw new InvalidOperationException(Missing);
                await FaultAsync(store, request, outcomeKey, originalOutcome[..^MissingNativeSuffixBytes], token).ConfigureAwait(false);
                await FaultAsync(store, request, locatorKey, KeySpace.PartitionOutcome(
                    request.ControlPartition, principal, request.CommandId), token).ConfigureAwait(false);
                var outcome = ReadRequired(store, request, token);
                await SqlRf3Protocol.EqualAsync(original, outcome.Result.Get<EventVectorControlResult>().OriginalPublicResultBytes.IsEmpty
                    ? throw new InvalidOperationException(Missing)
                    : EventVectorOriginalPublicResult.Read(outcome.Result.Get<EventVectorControlResult>(),
                        ReadPhase(store, request), new(IntegrationExecutionOptions.DatabaseLimits())));
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (store is { } owned)
            {
                ServerFailureObserver.Observe(owned.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task FaultAsync(ZoneTreeStore store, EventFeedControlRequest request,
        byte[] key, byte[]? fault, CancellationToken token)
    {
        var before = EventFeedReferencedOutcomeRf3Image.Capture(store, token);
        var original = store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(Missing);
        var failures = new List<Exception>();
        var changed = false;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                changed = true;
                ApplyFault(store, key, original, fault);
                var faulted = EventFeedReferencedOutcomeRf3Image.Capture(store, token);
                await Assert.That(faulted.Position).IsEqualTo(checked(before.Position + OneCommit));
                await SqlRf3Protocol.EqualAsync(ExpectedFaultRows(before.Rows, key, original, fault), faulted.Rows);
                await Assert.That(ReadGenericRefusal(store, request)?.Code).IsEqualTo(ErrorCode.Corruption);
                var refusal = ReadRefusal(store, request, token);
                await Assert.That(refusal?.Code).IsEqualTo(ErrorCode.RecoveryRequired);
                await Assert.That((refusal?.InnerException as KeyLoadException)?.Code).IsEqualTo(ErrorCode.Corruption);
                await SqlRf3Protocol.EqualAsync(faulted, EventFeedReferencedOutcomeRf3Image.Capture(store, token));
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (changed)
            {
                ServerFailureObserver.Observe(() => RestoreFault(store, key, original, fault), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var repaired = EventFeedReferencedOutcomeRf3Image.Capture(store, token);
        await Assert.That(repaired.Position).IsEqualTo(checked(before.Position + OneCommit + OneCommit));
        await SqlRf3Protocol.EqualAsync(before.Rows, repaired.Rows);
        _ = ReadRequired(store, request, token);
    }

    private static string[] ExpectedFaultRows(string[] rows, byte[] key, byte[] original, byte[]? fault)
    {
        var prefix = Convert.ToHexString(key) + ":";
        var expected = prefix + Convert.ToHexString(original);
        if (rows.Count(row => row == expected) != OneCommit)
        {
            throw new InvalidOperationException(Missing);
        }
        return fault is null ? rows.Where(row => row != expected).ToArray()
            : rows.Select(row => row == expected ? prefix + Convert.ToHexString(fault) : row).ToArray();
    }

    private static void ApplyFault(ZoneTreeStore store, byte[] key, byte[] original, byte[]? fault)
        => store.Commit((transaction, _) =>
        {
            var current = transaction.ReadOwnedValue(key);
            if (current is null || !current.AsSpan().SequenceEqual(original))
            {
                throw new InvalidOperationException(Missing);
            }
            if (fault is null)
            {
                transaction.Delete(key);
            }
            else
            {
                transaction.Put(key, fault);
            }
            return true;
        });

    private static void RestoreFault(ZoneTreeStore store, byte[] key, byte[] original, byte[]? fault)
        => store.Commit((transaction, _) =>
        {
            var current = transaction.ReadOwnedValue(key);
            if (current is not null && current.AsSpan().SequenceEqual(original))
            {
                return false;
            }
            if (fault is null ? current is not null : current is null || !current.AsSpan().SequenceEqual(fault))
            {
                throw new InvalidOperationException(Missing);
            }
            transaction.Put(key, original);
            return true;
        });

    private static KeyLoadException? ReadGenericRefusal(ZoneTreeStore store, EventFeedControlRequest request)
    {
        try
        {
            _ = store.Read(view =>
            {
                var principal = PartitionMovementPublicParentRf3Administrator.PrincipalId;
                var index = view.GetRecord<EventVectorParentOutcomeIndex>(
                    EventVectorKeys.ParentOutcome(request.ControlPartition, principal, request.CommandId))
                    ?? throw new InvalidOperationException(Missing);
                return CommandOutcomeKeyResolver.Select(view, principal, index.ActualTerminalControlPhaseId,
                    new(CommandOutcomeScopeKind.Partition, request.ControlPartition));
            });
        }
        catch (KeyLoadException failure) { return failure; }
        return null;
    }

    private static KeyLoadException? ReadRefusal(ZoneTreeStore store, EventFeedControlRequest request, CancellationToken token)
    {
        try
        {
            _ = ReadRequired(store, request, token);
        }
        catch (KeyLoadException failure) { return failure; }
        return null;
    }

    private static StoredOutcome ReadRequired(ZoneTreeStore store, EventFeedControlRequest request, CancellationToken token)
        => store.Read(view =>
        {
            token.ThrowIfCancellationRequested();
            var principalId = PartitionMovementPublicParentRf3Administrator.PrincipalId;
            var principal = view.GetRecord<PrincipalRecord>(KeySpace.Principal(principalId)) ?? throw new InvalidOperationException(Missing);
            var options = IntegrationExecutionOptions.DatabaseLimits();
            var admission = new EventVectorAdmissionPolicy(options);
            var index = view.GetRecord<EventVectorParentOutcomeIndex>(
                EventVectorKeys.ParentOutcome(request.ControlPartition, principalId, request.CommandId))
                ?? throw new InvalidOperationException(Missing);
            if (index.PrincipalId != principalId || index.OriginalPublicCommandId != request.CommandId
                || index.ControlPartition != request.ControlPartition || index.MapId != request.MapId
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    EventVectorControlBodyProjection.RequestDigest(request, admission).Span,
                    index.OriginalPublicRequestDigest.Span))
            {
                throw new InvalidOperationException(Missing);
            }
            EventVectorParentScopes.Authorize(new AuthorizationPolicy(), principal, index.OriginalAuthorizedSourceScopes, admission);
            admission.RequireEncodedBytes(index.OriginalNativeTerminalPhaseBytes.Length);
            var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(index.OriginalNativeTerminalPhaseBytes.Span);
            return EventVectorParentOutcome.Require(view, principal, phase, index.ActualTerminalControlPhaseId,
                index.ActualTerminalOutcomeFingerprint, store.Identity.Incarnation, admission);
        });

    private static EventVectorControlPhase ReadPhase(ZoneTreeStore store, EventFeedControlRequest request)
        => store.Read(view => NativeSerialization.Deserialize<EventVectorControlPhase>(
            (view.GetRecord<EventVectorParentOutcomeIndex>(EventVectorKeys.ParentOutcome(request.ControlPartition,
                PartitionMovementPublicParentRf3Administrator.PrincipalId, request.CommandId))
                ?? throw new InvalidOperationException(Missing)).OriginalNativeTerminalPhaseBytes.Span));
}
