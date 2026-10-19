using KeyLoad.Clustering;
using KeyLoad.Server;
using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Interfaces;
using ManagedCode.Orleans.Graph.Models;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

/// <summary>Checks the production execution storage policy, refused direct calls and original signed command replay.</summary>
internal sealed class ConnectionGraphPolicyTests
{
    private const string Document = "execution-graph-policy-following";
    private const long FirstRevision = 1;

    [Test]
    public Task ActualServerPolicyAllowsOnlySelectedPartitionMethodsAndPreservesSignedReplay()
    {
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            await Assert.That(scenario.Fixture.Hosts.Nodes.Length)
                .IsEqualTo(ConnectionServerEvidence.RuntimeExpectedFiles);
            foreach (var node in scenario.Fixture.Hosts.Nodes)
            {
                var manager = node.Services.GetRequiredService<GrainTransitionManager>();
                await PolicyAsync(manager);
            }
            await DirectRefusalsAsync(scenario, token);
            await HealthyAsync(scenario, token);
            await scenario.CloseAsync(token);
        });
    }

    private static async Task PolicyAsync(GrainTransitionManager manager)
    {
        var connection = typeof(IConnectionGrain).FullName!;
        var worker = typeof(IRequestWorkerGrain).FullName!;
        var partition = typeof(ICommandPartitionGrain).FullName!;
        var commit = typeof(IDatabaseCommitGrain).FullName!;
        var execute = nameof(IConnectionGrain.ExecuteStreamAsync);
        await Assert.That(manager.IsTransitionAllowed(Transition(connection, execute,
            commit, nameof(IDatabaseCommitGrain.GetReadOwnerAsync)))).IsTrue();
        await Assert.That(manager.IsTransitionAllowed(Transition(connection, nameof(IConnectionGrain.CloseAsync),
            commit, nameof(IDatabaseCommitGrain.GetReadOwnerAsync)))).IsFalse();
        await Assert.That(manager.IsTransitionAllowed(Transition(connection, execute,
            partition, nameof(ICommandPartitionGrain.ExecuteAsync)))).IsTrue();
        await Assert.That(manager.IsTransitionAllowed(Transition(worker, nameof(IRequestWorkerGrain.ExecuteAsync),
            partition, nameof(ICommandPartitionGrain.ExecuteAsync)))).IsFalse();
        await Assert.That(manager.IsTransitionAllowed(Transition(worker, nameof(IRequestWorkerGrain.ExecuteCommandAsync),
            partition, nameof(ICommandPartitionGrain.ExecuteAsync)))).IsFalse();
        await Assert.That(manager.IsTransitionAllowed(Transition(partition, nameof(ICommandPartitionGrain.ExecuteAsync),
            commit, nameof(IDatabaseCommitGrain.GetReadOwnerAsync)))).IsTrue();
        await Assert.That(manager.IsTransitionAllowed(Client(partition, nameof(ICommandPartitionGrain.ExecuteAsync)))).IsFalse();
        await Assert.That(manager.IsTransitionAllowed(Client(connection, execute))).IsTrue();
        await Assert.That(manager.IsTransitionAllowed(Transition(RuntimeJournalClient.CallerIdentity,
            RuntimeJournalClient.SendCommandCallerMethod, connection, execute))).IsTrue();
        await WorkerPolicyAsync(manager);
    }

    private static async Task WorkerPolicyAsync(GrainTransitionManager manager)
    {
        var connection = typeof(IConnectionGrain).FullName!;
        var worker = typeof(IRequestWorkerGrain).FullName!;
        var partition = typeof(ICommandPartitionGrain).FullName!;
        var service = typeof(PartitionStorageGrainService).FullName!;
        foreach (var method in new[] { nameof(IRequestWorkerGrain.ExecuteAsync), nameof(IRequestWorkerGrain.ExecuteCommandAsync) })
        {
            await Assert.That(manager.IsTransitionAllowed(Transition(service, Constants.AnyMethod, worker, method)))
                .IsFalse();
            await Assert.That(manager.IsTransitionAllowed(Transition(connection,
                nameof(IConnectionGrain.ExecuteStreamAsync), worker, method)))
                .IsEqualTo(method == nameof(IRequestWorkerGrain.ExecuteAsync));
            await Assert.That(manager.IsTransitionAllowed(Transition(connection,
                nameof(IConnectionGrain.CloseAsync), worker, method))).IsFalse();
            await Assert.That(manager.IsTransitionAllowed(Transition(partition,
                nameof(ICommandPartitionGrain.ExecuteAsync), worker, method)))
                .IsEqualTo(method == nameof(IRequestWorkerGrain.ExecuteCommandAsync));
            await Assert.That(manager.IsTransitionAllowed(Transition(Constants.UnknownCallerId,
                Constants.AnyMethod, worker, method))).IsFalse();
            await Assert.That(manager.IsTransitionAllowed(Client(worker, method))).IsFalse();
        }
        await Assert.That(manager.IsTransitionAllowed(Transition(service, Constants.AnyMethod,
            worker, nameof(IConnectionGrain.CloseAsync)))).IsFalse();
        await Assert.That(manager.IsTransitionAllowed(Transition(worker, nameof(IRequestWorkerGrain.ExecuteAsync),
            connection, nameof(IConnectionGrain.ExecuteStreamAsync)))).IsFalse();
    }

    private static async Task DirectRefusalsAsync(ConnectionScenario scenario, CancellationToken token)
    {
        var principal = scenario.Principal();
        var command = scenario.Command(Document);
        var requestId = Guid.NewGuid();
        var signed = scenario.Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch,
            command.CommandId, OrleansSerialization.Serialize(command));
        var partition = GrainPartitionResolver.Resolve(scenario.Fixture.Codec.Verify(signed));
        var grains = scenario.Fixture.Cluster.Client;
        var cut = scenario.Fixture.Database.ReadCanonicalCut();
        using var identity = new GrainRequestIdentityScope(scenario.Fixture.Cluster.ServiceProvider,
            principal, requestId, command.CommandId, token, connectionId: scenario.Fixture.ConnectionId);
        var worker = grains.GetGrain<IRequestWorkerGrain>(RequestWorkerIdentity.ForRequests());
        var owner = scenario.Fixture.Hosts.Nodes[0].Services.GetRequiredService<global::Orleans.Runtime.ILocalSiloDetails>().SiloAddress;
        var workerFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => worker.ExecuteCommandAsync(owner, signed, partition, token));
        await Assert.That(workerFailure!.Message).IsEqualTo(
            $"Transition from {Constants.ClientCallerId} to {typeof(IRequestWorkerGrain).FullName} is not allowed.");
        var partitionFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => grains.GetGrain<ICommandPartitionGrain>(partition).ExecuteAsync(signed, token));
        await Assert.That(partitionFailure!.Message).IsEqualTo(
            $"Transition from {Constants.ClientCallerId} to {typeof(ICommandPartitionGrain).FullName} is not allowed.");
        await Assert.That(scenario.Fixture.Database.ReadCanonicalCut()).IsEqualTo(cut);
        await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command, Document);
    }

    private static async Task HealthyAsync(ConnectionScenario scenario, CancellationToken token)
    {
        var principal = scenario.Principal();
        var command = scenario.Command(Document);
        var originalId = Guid.NewGuid();
        var original = await scenario.CommandAsync(principal, originalId, command, token);
        await Assert.That(original.Error).IsNull();
        await Assert.That(original.SafeDetail).IsNull();
        await Assert.That(ConnectionAssertions.Value<CommitReceipt>(original).CommandId)
            .IsEqualTo(command.CommandId);
        await DocumentAsync(scenario);
        var cut = scenario.Fixture.Database.ReadCanonicalCut();
        var position = scenario.Fixture.Database.Store.Position;
        var replayId = Guid.NewGuid();
        var replay = await scenario.CommandAsync(principal, replayId, command, token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(replay.SafeDetail).IsEqualTo(original.SafeDetail);
        await Assert.That(replay.Payload.Span.SequenceEqual(original.Payload.Span)).IsTrue();
        await Assert.That(scenario.Fixture.Database.ReadCanonicalCut()).IsEqualTo(cut);
        await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
        await DocumentAsync(scenario);
        await ConnectionAssertions.SameOwnerAsync(scenario, originalId, replayId, token);
    }

    private static async Task DocumentAsync(ConnectionScenario scenario)
    {
        await ConnectionAssertions.StoredAsync(scenario, Document, FirstRevision, ConnectionProtocol.FirstJson);
        await Assert.That(scenario.Stored(Document)!.Redacted).IsFalse();
        await Assert.That(scenario.Stored(Document)!.RedactedFields.IsEmpty).IsTrue();
    }

    // These are execution policy inputs, not simulated grain invocations or authority witnesses.
    private static CallHistory Transition(string source, string sourceMethod, string target, string targetMethod)
    {
        var history = new CallHistory();
        history.Push(new OutCall(null, null, source, target, targetMethod, sourceMethod));
        history.Push(new InCall(null, null, target, targetMethod));
        return history;
    }

    private static CallHistory Client(string target, string method)
    {
        var history = new CallHistory();
        history.Push(new InCall(null, null, target, method));
        return history;
    }
}
