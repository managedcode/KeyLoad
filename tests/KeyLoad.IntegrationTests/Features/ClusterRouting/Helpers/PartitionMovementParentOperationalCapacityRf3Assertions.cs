using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>All effects and quota stay exact; only authenticated native election no-ops may advance the applied cut.</summary>
internal static class PartitionMovementParentOperationalCapacityRf3Assertions
{
    internal static async Task RequireRestoredAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementPublicParentRf3NativeCut[] original)
    {
        foreach (var before in original)
        {
            var after = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, before.Node, seed.FirstRequest, Guid.Empty);
            await PartitionMovementCapturePointerRf3Cut.RequireExactRemainingRowsAsync(before, after, new(StringComparer.Ordinal));
            await Assert.That(after.StorePosition).IsEqualTo(before.StorePosition);
        }
    }

    internal static async Task RequireRefusedAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var resume = seed.FirstRequest with { Mode = PartitionMoveMode.Resume };
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(PartitionMovePublicProtocol.ToolName,
            resume, cancellationToken).ConfigureAwait(false), ErrorCode.BudgetExceeded, dispatched: true);
        var sql = SqlRf3Protocol.Call(resume.Partition, PartitionMovePublicProtocol.ToolName, resume);
        var rejectedSql = await seed.Source.ExecuteSqlAsync(sql, cancellationToken).ConfigureAwait(false);
        await Assert.That(rejectedSql.IsFailed).IsTrue();
        await Assert.That(rejectedSql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(SqlOperationProtocol.ToolName,
            sql, cancellationToken).ConfigureAwait(false), ErrorCode.BudgetExceeded, dispatched: true);
    }

    internal static async Task RequireNoEffectsAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3NativeCut[] before, PartitionMovementPublicParentRf3NativeCut[] after)
    {
        foreach (var original in before)
        {
            var actual = after.Single(cut => cut.Node == original.Node);
            var entries = PartitionMovementCapturePointerRf3Fault.AppliedEntries(wave, original, actual);
            await Assert.That(entries.All(entry => entry.Operation is null)).IsTrue();
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (entries.Length != PartitionMoveProtocol.EmptyCount)
            { allowed.Add(Convert.ToHexString(KeySpace.AppliedBytes)); }
            await PartitionMovementCapturePointerRf3Cut.RequireExactRemainingRowsAsync(original, actual, allowed);
            await Assert.That(actual.StorePosition - original.StorePosition).IsEqualTo((long)entries.Length);
            await SqlRf3Protocol.EqualAsync(original.Header, actual.Header);
            await SqlRf3Protocol.EqualAsync(original.Pending, actual.Pending);
            await SqlRf3Protocol.EqualAsync(original.LastIssued, actual.LastIssued);
        }
    }

    internal static async Task RequireHealthyAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveAuthorizeBody inspected,
        CancellationToken cancellationToken)
    {
        var freshStart = TimeProvider.System.GetUtcNow();
        var terminal = await PartitionMovementParentOperationalCapacityRf3Producer.ResumeAtPreflightAsync(wave,
            seed, null, cancellationToken) ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, seed.FirstRequest,
            seed.OriginalPlacement, seed.Directory.ControlOwner, terminal, cancellationToken);
        await seed.VerifyAsync(cancellationToken);
        var identity = PartitionMovementParentOperationalCapacityRf3Producer.StageGrantId(seed.FirstRequest);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            identity, cancellationToken);
        await RequireFreshPageGrantAsync(before, inspected, freshStart);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest,
            terminal, cancellationToken);
        await seed.VerifyAsync(cancellationToken);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            identity, cancellationToken);
        await RequireNoEffectsAsync(wave, before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
    }

    private static async Task RequireFreshPageGrantAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveAuthorizeBody inspected, DateTimeOffset freshStart)
    {
        foreach (var cut in cuts.Where(cut => cut.Header is not null))
        {
            var phase = cut.OriginalPhase ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
            var outcome = phase.OriginalResult ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
            await Assert.That(outcome.Error).IsNull();
            await Assert.That(phase.ObservationCheckpointReceipt).IsNotNull();
            var actual = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(phase.OriginalPhase!.Body.Span);
            await SqlRf3Protocol.EqualAsync(inspected.Phase, actual.Phase);
            await SqlRf3Protocol.EqualAsync(inspected.ReceiverOwner, actual.ReceiverOwner);
            await Assert.That(actual.ExpiresAt).IsGreaterThan(inspected.ExpiresAt);
            await Assert.That(actual.ExpiresAt).IsGreaterThan(freshStart);
            var grant = outcome.Get<PartitionMovePhaseResult>().Grant!;
            await Assert.That(grant.GrantId).IsEqualTo(actual.GrantId);
            await Assert.That(grant.PhaseCommandId).IsEqualTo(actual.PhaseCommandId);
            await Assert.That(grant.ExpiresAt).IsEqualTo(actual.ExpiresAt);
        }
    }
}
