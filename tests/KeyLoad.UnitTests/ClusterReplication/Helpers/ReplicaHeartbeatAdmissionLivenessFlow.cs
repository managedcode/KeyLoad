using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using D = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaHeartbeatAdmissionProtocol;
using A = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaHeartbeatAdmissionLivenessAssertions;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaHeartbeatAdmissionLivenessFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var profile = new RequestCqrsHostedProfile(TimeProvider.System, UnitRoutingOptions.Routing(),
                null, null, false, false, null);
            await using var fixture = new RequestCqrsCohortRuntimeFixture(hosted: profile);
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture, failures, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(RequestCqrsCohortRuntimeFixture fixture,
        List<Exception> failures, CancellationToken token)
    {
        await fixture.InitializeAsync(token);
        using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var setup = await ConfigureAsync(fixture, linked.Token);
        var leader = setup.Leader;
        var followers = ReplicaBarrierDeadlineFlow.Voters.Select(fixture.Node).Where(node => node != leader).ToArray();
        await Assert.That(followers.Length).IsEqualTo(D.Two);
        using var exchanges = new ReplicaHeartbeatNativeExchangeObservation(
            followers[D.Empty].Services.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToParsableString(),
            followers[D.One].Services.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToParsableString());
        await ServerFailureObserver.ObserveAsync(() => HeldAsync(fixture, leader, setup.Lifetime, exchanges, failures, linked.Token), failures);
    }

    private static async Task<(RequestCqrsCohortNativeNode Leader, ResourceLifetimeV1 Lifetime)> ConfigureAsync(
        RequestCqrsCohortRuntimeFixture fixture, CancellationToken token)
    {
        var leader = await ReplicaBarrierDeadlineFlow.LeaderAsync(fixture, token);
        var database = leader.Journal.Database;
        var definition = new ResourceDefinition(D.Collection, ResourceKind.Collection, D.Partition.TransactionDomainId);
        var request = new ConfigureResourceRequest(D.Partition.TenantId, D.Partition.DatabaseId, definition);
        var creation = database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(), D.Root,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        await A.EqualAsync(definition, (await leader.Hosted!.Coordinator.SubmitVerifiedAsync(creation, token)).Get<ResourceDefinition>());
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            await fixture.Node(voter).Journal.Consensus.ReadBarrierAsync(token);
        }
        return (await ReplicaBarrierDeadlineFlow.LeaderAsync(fixture, token),
            ResourceLifetimeIdentity.Create(creation, request.TenantId, request.DatabaseId, definition));
    }

    private static async Task HeldAsync(RequestCqrsCohortRuntimeFixture fixture, RequestCqrsCohortNativeNode leader,
        ResourceLifetimeV1 lifetime, ReplicaHeartbeatNativeExchangeObservation exchanges,
        List<Exception> failures, CancellationToken token)
    {
        var database = leader.Journal.Database;
        var clock = new ReplicaHeartbeatAdmissionClock(token);
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        var request = new CommandRequest(Guid.NewGuid(), D.Partition,
            [new PutDocument(D.Collection, D.Document, D.FirstJson, D.Empty)]);
        var original = database.CreateNativeOperation(OperationKind.Batch, request.CommandId, D.Root,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request), work);
        var encoded = NativeSerialization.Serialize(original);
        var before = ReplicaBarrierDeadlineFlow.Voters.ToDictionary(voter => voter,
            voter => ReplicaBarrierDeadlineAssertions.Image(fixture.Node(voter)), StringComparer.Ordinal);
        var state = await leader.Journal.Consensus.StateAsync(token);
        var spent = work.ReadBytes;
        clock.Arm();
        var submitted = Task.Run(() => leader.Journal.Consensus.SubmitAsync(original, null, work, token), CancellationToken.None);
        OperationResult? result = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var winner = await Task.WhenAny(submitted, clock.Arrived).WaitAsync(token);
                if (ReferenceEquals(winner, submitted))
                {
                    _ = await submitted;
                    throw new InvalidOperationException(D.Missing);
                }
                exchanges.Arm(clock.ArrivalUtc);
                await A.HeldAsync(fixture, leader, submitted, state, before, token);
                await Assert.That(clock.IsHeld).IsTrue();
                await Assert.That(work.ReadBytes).IsEqualTo(spent);
                exchanges.RequireHealthy();
                await Assert.That(exchanges.Counts.First).IsGreaterThan((long)D.Empty);
                await Assert.That(exchanges.Counts.Second).IsGreaterThan((long)D.Empty);
            }, failures);
        }
        finally
        {
            exchanges.Stop();
            clock.Release();
            await ServerFailureObserver.ObserveAsync(async () => { result = await submitted; }, failures);
        }
        if (failures.Count != D.Empty)
        {
            return;
        }
        await Assert.That(NativeSerialization.Serialize(original).SequenceEqual(encoded)).IsTrue();
        await A.CommittedAsync(fixture, leader, original, result!.Get<CommitReceipt>(), lifetime,
            state.LastIndex + D.One, D.FirstRevision, D.FirstJson, token);
        await A.ReplayAndAdvanceAsync(fixture, leader, original, result.Get<CommitReceipt>(), lifetime, token);
    }
}
