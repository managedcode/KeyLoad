using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateScenario
{
    internal static async Task RunAsync(int nodes, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30), TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = await FunctionalGateFixture.StartAsync(nodes, lifetime.Token);
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture, lifetime.Token), failures);
            fixture.PreserveEvidence = failures.Count != 0;
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<bool> ExecuteAsync(FunctionalGateFixture fixture, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[0], fixture.AdminKey, async administrator =>
        {
            var suffix = Guid.NewGuid().ToString(FunctionalGateProtocol.GuidFormat);
            var partition = new PartitionRef(FunctionalGateProtocol.TenantPrefix + suffix,
                FunctionalGateProtocol.Database, FunctionalGateProtocol.TransactionDomain, suffix);
            var anchor = new EntityRef(partition, FunctionalGateProtocol.AnchorCollection, FunctionalGateProtocol.AnchorId);
            var bootstrap = new FunctionalGateContext(administrator, administrator, administrator,
                partition, anchor, FunctionalGateProtocol.ResourceCount)
            {
                VoterCount = fixture.NodeNames.Length
            };
            await FunctionalGateNodeAssertions.VerifyAsync(fixture, bootstrap, token);
            await FunctionalGateDocuments.InitializeAsync(bootstrap, token);
            bootstrap = bootstrap with
            {
                Anchor = bootstrap.Bind(bootstrap.Anchor)
            };
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var users = await FunctionalGateUsers.CreateAsync(fixture, administrator, partition, token);
                await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, bootstrap, users, token), failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);

    private static async Task ExerciseAsync(FunctionalGateFixture fixture, FunctionalGateContext bootstrap,
        FunctionalGateUsers users, CancellationToken token)
    {
        var writers = users.Writers;
        var readers = users.Readers;
        var writerIds = users.WriterPrincipalIds;
        var readerIds = users.ReaderPrincipalIds;
        await Assert.That(writers.Count).IsEqualTo(FunctionalGateProtocol.UserPairs);
        await Assert.That(readers.Count).IsEqualTo(FunctionalGateProtocol.UserPairs);
        var contexts = Enumerable.Range(0, FunctionalGateProtocol.UserPairs).Select(index =>
            new FunctionalGateContext(bootstrap.Administrator, writers[index], readers[index], bootstrap.Partition,
                bootstrap.Anchor, bootstrap.ResourceCount)
            {
                ReferenceBindings = bootstrap.ReferenceBindings,
                QueueAdministrator = users.QueueAdministrator,
                WriterPrincipalId = writerIds[index],
                ReaderPrincipalId = readerIds[index],
                VoterCount = fixture.NodeNames.Length
            }).ToArray();
        await InitializeRemainingAsync(contexts[0], token);
        foreach (var context in contexts)
        {
            await FunctionalGateUserAnalytics.InitializeAsync(context, token);
        }
        await users.VerifyAsync(contexts[0], token);
        await FunctionalGateOperationalAnalytics.ExerciseAsync(contexts[0], token);
        await FunctionalGateExecution.RunAsync(contexts, token);
        await FunctionalGateQueueGraph.RunAsync(contexts[0], token);
        await FunctionalGateQueues.VerifyBaselineAsync(contexts[0], token);
        await FunctionalGateDocumentsReads.AnchorAsync(contexts[0], token);
        await FunctionalGateAnalyticsNormalPolicy.RunAsync(contexts, users.WriterPrincipals, token);
        foreach (var context in contexts)
        {
            await FunctionalGateAnalyticsMixedPublic.RunAsync(context,
                verifyIncidentBound: ReferenceEquals(context, contexts[^1]), token);
        }
        await FunctionalGateNodeAssertions.VerifyAsync(fixture, contexts[0], token);
        var dropped = await FunctionalGateDrop.RunAsync(contexts[0], token);
        await FunctionalGateDrop.VerifyAsync(contexts[0], dropped, token);
        await FunctionalGateCompletionAssertions.RequireAsync(contexts);
        await Console.Out.WriteLineAsync($"Functional gate completed: nodes={fixture.NodeNames.Length} workloadUsers={writers.Count + readers.Count}"
            + $" operationalUsers=1 resourcesPerModel={bootstrap.ResourceCount} sdk={contexts.Sum(context => context.SdkOperations)}"
            + $" mcp={contexts.Sum(context => context.McpOperations)}");
    }

    private static async Task InitializeRemainingAsync(FunctionalGateContext context, CancellationToken token)
    {
        await FunctionalGateRelational.InitializeAsync(context, token);
        await FunctionalGateGraphs.InitializeAsync(context, token);
        await FunctionalGateSearch.InitializeAsync(context, token);
        await FunctionalGateEvents.InitializeAsync(context, token);
        await FunctionalGateQueues.InitializeAsync(context, token);
        await FunctionalGateBlobs.InitializeAsync(context, token);
        await FunctionalGateAnalytics.InitializeAsync(context, token);
        await FunctionalGateComposition.InitializeAsync(context, token);
    }
}
