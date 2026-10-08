using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

[NotInParallel]
internal sealed class DocumentSessionIsolationRf3Tests
{
    [Test]
    public async Task Kl021ReachableFormerLeaderRejectsMinimumTokenWhileOtherVotersAcknowledgeNewerTerm()
    {
        var failures = new List<Exception>();
        string? root = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var lifecycle = new ReplicaIsolationFixtureLifecycle();
            await lifecycle.InitializeAsync().ConfigureAwait(false);
            var fixture = lifecycle.Fixture;
            root = fixture.Root;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var deadline = McpCallerDeadline.Create();
                await ReplicaIsolationFlow.RunAsync(fixture, deadline.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count == ReplicaIsolationFlowProtocol.Zero)
        { await Assert.That(Directory.Exists(root)).IsFalse(); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
