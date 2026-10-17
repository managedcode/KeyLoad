using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaRetainedReplayFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsCohortRuntimeFixture(barrierDeadline: new ReplicaBarrierDeadlineScenario());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
                using var work = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                await ExerciseAsync(fixture, work.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(RequestCqrsCohortRuntimeFixture fixture, CancellationToken token)
    {
        var leader = await ReplicaBarrierDeadlineFlow.LeaderAsync(fixture, token);
        await leader.Journal.Consensus.ReadControlBarrierAsync(token);
        await ReplicaBarrierDeadlineAssertions.SettingsAsync(fixture);
        var seed = await ReplicaRetainedReplayProducer.CreateAsync(leader, token);
        await SyncAsync(fixture, token);
        await ReplicaRetainedReplayAssertions.ModelsAsync(fixture, seed, originalPolicyCurrent: true, token);
        foreach (var command in seed.Commands)
        {
            var cuts = ReplicaRetainedReplayAssertions.Capture(fixture);
            var actual = await leader.Journal.Consensus.SubmitAsync(command.Operation, token);
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(command.Result, actual);
            await ReplicaRetainedReplayAssertions.UnchangedAsync(fixture, cuts, sameTerm: true);
        }
        await ReplicaRetainedReplayAuthority.ChangedAsync(fixture, leader, seed.Commands.First(), token);
        await ReplicaRetainedReplayAuthority.RevokeAndRepairAsync(fixture, leader, seed.Commands.First(), token);
        var healthy = await ReplicaRetainedReplayProducer.DocumentAsync(leader,
            ReplicaRetainedReplayProducer.HealthyDocument, ReplicaRetainedReplayProducer.HealthyJson, token);
        await SyncAsync(fixture, token);
        await ReplicaRetainedReplayQuorum.RefuseThenRepairAsync(fixture, leader, healthy, token);
        // Each restarted voter retains its original roots, identity and acknowledged native prefix.
        await ReplicaRetainedReplayAssertions.ModelsAsync(fixture, seed, originalPolicyCurrent: false, token);
    }

    internal static async Task SyncAsync(RequestCqrsCohortRuntimeFixture fixture, CancellationToken token)
    {
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            try
            {
                await fixture.Node(voter).Journal.Consensus.ReadControlBarrierAsync(token);
            }
            catch (Exception primary)
            {
                var failures = new List<Exception> { primary };
                await ReplicaRetainedReplayFailureObservation.ReportAsync(fixture, voter, primary, failures, token);
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
        }
    }
}
