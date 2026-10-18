using KeyLoad.Clustering;
using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Interfaces;
using ManagedCode.Orleans.Graph.Models;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks the actual six-server default-deny policy and its original signed command path.</summary>
internal sealed class ConnectionNativeGraphPolicyTests
{
    private const string Document = "native-graph-policy-following";
    private const long FirstRevision = 1;

    [Test]
    public Task ActualServerPolicyAllowsOnlySelectedPartitionMethodsAndPreservesSignedReplay()
    {
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            await Assert.That(scenario.Fixture.Hosts.Nodes.Count)
                .IsEqualTo(ConnectionNativeServerEvidence.ExpectedNativeFiles);
            foreach (var node in scenario.Fixture.Hosts.Nodes)
            {
                var manager = node.Application.Services.GetRequiredService<GrainTransitionManager>();
                await PolicyAsync(manager);
            }
            await HealthyAsync(scenario, token);
            await scenario.CloseAsync(token);
        });
    }

    private static async Task PolicyAsync(GrainTransitionManager manager)
    {
        var connection = typeof(IConnectionGrain).FullName!;
        var partition = typeof(ICommandPartitionGrain).FullName!;
        var execute = nameof(IConnectionGrain.ExecuteStreamAsync);
        foreach (var method in new[]
        {
            nameof(ICommandPartitionGrain.ExecuteAsync),
            nameof(ICommandPartitionGrain.PrepareOriginalCommandAsync)
        })
        {
            await Assert.That(manager.IsTransitionAllowed(Transition(connection, execute, partition, method))).IsTrue();
            await Assert.That(manager.IsTransitionAllowed(Transition(connection,
                nameof(IConnectionGrain.CloseAsync), partition, method))).IsFalse();
            await Assert.That(manager.IsTransitionAllowed(Transition(Constants.UnknownCallerId,
                execute, partition, method))).IsFalse();
            await Assert.That(manager.IsTransitionAllowed(Client(partition, method))).IsFalse();
        }
        await Assert.That(manager.IsTransitionAllowed(Transition(connection, execute,
            partition, nameof(IConnectionGrain.CloseAsync)))).IsFalse();
        await Assert.That(manager.IsTransitionAllowed(Client(connection, execute))).IsTrue();
        await Assert.That(manager.IsTransitionAllowed(Transition(RuntimeJournalClient.CallerIdentity,
            RuntimeJournalClient.SendCommandCallerMethod, connection, execute))).IsTrue();
    }

    private static async Task HealthyAsync(ConnectionNativeScenario scenario, CancellationToken token)
    {
        var principal = scenario.Principal();
        var command = scenario.Command(Document);
        var originalId = Guid.NewGuid();
        var original = await scenario.CommandAsync(principal, originalId, command, token);
        await Assert.That(original.Error).IsNull();
        await Assert.That(original.SafeDetail).IsNull();
        await Assert.That(ConnectionNativeAssertions.Value<CommitReceipt>(original).CommandId)
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
        await ConnectionNativeAssertions.SameOwnerAsync(scenario, originalId, replayId, token);
    }

    private static async Task DocumentAsync(ConnectionNativeScenario scenario)
    {
        await ConnectionNativeAssertions.StoredAsync(scenario, Document, FirstRevision, ConnectionNativeProtocol.FirstJson);
        await Assert.That(scenario.Stored(Document)!.Redacted).IsFalse();
        await Assert.That(scenario.Stored(Document)!.RedactedFields.IsEmpty).IsTrue();
    }

    // These are native policy inputs, not simulated grain invocations or authority witnesses.
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
