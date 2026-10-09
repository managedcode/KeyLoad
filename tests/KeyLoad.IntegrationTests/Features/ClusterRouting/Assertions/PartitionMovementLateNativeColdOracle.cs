using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Same immutable factory seal, real recovered owner and exact failed native commit across cold reopen.</summary>
internal static class PartitionMovementLateNativeColdOracle
{
    private const int FirstOwner = 0;
    internal static async Task RequireAsync(PartitionMovementLateNativeOwners owners,
        PartitionMovementLateNativeCaptured captured, CancellationToken cancellationToken)
    {
        var targetBefore = await ReadOtherOwnersAsync(owners, captured, cancellationToken);
        var source = owners.OpenStopped(captured.OwnerIndex);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var database = source.Partition.Database;
            var original = captured.Operation;
            var bytes = original.NativePayload.ToArray();
            var digest = SHA256.HashData(bytes);
            var principalId = database.Authenticate(owners.Settings.AdministratorKey, database.EvaluationClock.GetUtcNow());
            await Assert.That(principalId).IsEqualTo(original.PrincipalId);
            var principal = database.Store.Read(view => database.Principal(view, principalId, database.EvaluationClock.GetUtcNow()));
            await Assert.That(principal.ClusterAdministrator).IsTrue();
            _ = database.VerifyOperationAuthority(original);
            var before = PartitionMovementLateNativeCut.Read(source, original);
            var clockBefore = database.EvaluationClock.GetUtcNow();
            // A separate supporting canonical-apply scope; the canceled request is never resumed.
            using var nativeApply = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var result = database.ApplyEmbedded(original, nativeApply.Token);
            var clockAfter = database.EvaluationClock.GetUtcNow();
            var after = PartitionMovementLateNativeCut.Read(source, original);
            await PartitionMovementLateNativeCut.RequireFailureDeltaAsync(before, after, original, result);
            await Assert.That(Convert.ToHexString(SHA256.HashData(original.NativePayload.Span))).IsEqualTo(Convert.ToHexString(digest));
            await Assert.That(original.NativePayload.Span.SequenceEqual(bytes)).IsTrue();
            await Assert.That(clockAfter >= clockBefore).IsTrue();
            await Assert.That(after.Outcome!.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
            await Assert.That(after.Outcome.Fingerprint).IsEqualTo(JsonData.Fingerprint(new
            { original.Id, original.Kind, original.PrincipalId, original.PayloadJson }));
            var committed = database.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.ClockBytes)!));
            await Assert.That(committed >= clockBefore && committed <= clockAfter).IsTrue();
            await Assert.That(after.Outcome.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
            var selected = database.ResolveOutcome(original);
            await Assert.That(selected.Error).IsEqualTo(result.Error);
            await Assert.That(selected.SafeDetail).IsEqualTo(result.SafeDetail);
        }, failures);
        await CloseAsync(source, failures, cancellationToken);
        ServerFailureObserver.ThrowIfAny(failures);
        await RequirePersistedAsync(owners, captured, cancellationToken);
        var targetAfter = await ReadOtherOwnersAsync(owners, captured, cancellationToken);
        await Assert.That(targetAfter.Length == targetBefore.Length && targetAfter.Zip(targetBefore)
            .All(pair => pair.First.SequenceEqual(pair.Second, StringComparer.Ordinal))).IsTrue();
    }

    internal static async Task RequirePersistedAsync(PartitionMovementLateNativeOwners owners,
        PartitionMovementLateNativeCaptured captured, CancellationToken cancellationToken)
    {
        var source = owners.OpenStopped(captured.OwnerIndex);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = source.Partition.Database.VerifyOperationAuthority(captured.Operation);
            var cut = PartitionMovementLateNativeCut.Read(source, captured.Operation);
            await Assert.That(cut.Cancellation).IsNotNull();
            await Assert.That(cut.Outcome).IsNotNull();
            await Assert.That(cut.Outcome!.Result.Error).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(cut.Outcome.Result.SafeDetail).IsEqualTo(KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionMoveProtocol.MissingAuthority);
        }, failures);
        await CloseAsync(source, failures, cancellationToken);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<string[][]> ReadOtherOwnersAsync(PartitionMovementLateNativeOwners owners,
        PartitionMovementLateNativeCaptured captured, CancellationToken cancellationToken)
    {
        var rows = new List<string[]>();
        for (var index = FirstOwner; index < PartitionMovementLateNativeSettings.OwnerCount; index++)
        {
            if (index == captured.OwnerIndex)
            { continue; }
            var node = owners.OpenStopped(index);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => rows.Add(PartitionMovementLateNativeCut.Read(node, captured.Operation).Rows
                .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + pair.Value).ToArray()), failures);
            await CloseAsync(node, failures, cancellationToken);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return rows.ToArray();
    }

    private static async Task CloseAsync(PartitionMovementLateNativeNode owner, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        var services = owner.Application.Services;
        var limits = services.GetRequiredService<IOptions<ServerExecutionOptions>>().Value;
        using var deadline = new CancellationTokenSource(limits.ShutdownTimeout, services.GetRequiredService<TimeProvider>());
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        await ServerFailureObserver.ObserveAsync(() => owner.StopAsync(lifetime.Token), failures);
    }
}
