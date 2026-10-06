using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[NotInParallel("NativeDatabaseFlows")]
internal sealed class NativeDatabaseDocumentFlowTests
{
    [Test]
    [Arguments("SurrealDB")]
    [Arguments("HelixDB")]
    public async Task NativeDocumentsGraphAndOrderedReadbackUseActualServer(string name)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await NativeDatabaseFlowFixture.CreateAsync(name, token);
        using var client = await fixture.ClientAsync(token);
        var timeProvider = new NativeDatabaseFlowTimeProvider(TimeProvider.System);
        await using IComparisonTarget target = name == "SurrealDB"
            ? new SurrealDbTarget(client, Guid.NewGuid().ToString(), fixture.Image, NativeDatabaseFlowFixture.ExecutionOptions, timeProvider)
            : new HelixDbTarget(client, Guid.NewGuid().ToString(), fixture.Image, NativeDatabaseFlowFixture.ExecutionOptions, timeProvider);
        var corpus = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Comparisons.ComparisonOptions() { Documents = 32, Dimensions = 128, GraphVertices = 16, GraphDepth = 3 }));
        await target.InitializeAsync(corpus, token);
        await using var session = await target.OpenSessionAsync(token);
        var readback = new List<FoundDocument>();
        await foreach (var row in session.ReadCorpusAsync(token))
        {
            readback.Add(row);
        }

        await Assert.That(readback.Select(row => row.Id)).IsEquivalentTo(corpus.Documents.Select(row => row.Id));
        await Assert.That(readback.All(row => BenchmarkDataset.SameJson(row.Json, corpus.Documents.Single(doc => doc.Id == row.Id).Json))).IsTrue();
        var first = corpus.Documents[0];
        await NativeDatabaseClockFlow.VerifySessionReadAsync(session, first, timeProvider, token);
        foreach (var scenario in new[] { Scenario.GraphNeighbors, Scenario.GraphTraverse })
        {
            var actual = await session.ExecuteAsync(scenario, first, token);
            await Assert.That(actual.Vertices).IsEquivalentTo(corpus.Reachable(first, scenario == Scenario.GraphNeighbors ? 1 : 3));
        }
        var created = corpus.CreateDocument(400);
        await session.ExecuteAsync(Scenario.DocumentWrite, created, token);
        await Assert.That((await session.ReadAsync(created, token))!.Json).IsEqualTo(created.Json);
        await RequireCreateConflictAsync(session, created, token);
        await Assert.That((await session.ReadAsync(created, token))!.Json).IsEqualTo(created.Json);
        var initial = BenchmarkDataset.InitialMutationState(Scenario.DocumentUpdate, created);
        await session.ExecuteAsync(Scenario.DocumentUpdate, initial, token);
        await Assert.That((await session.ReadAsync(created, token))!.Json).IsEqualTo(initial.Json);
        await session.ExecuteAsync(Scenario.DocumentDelete, created, token);
        await Assert.That(await session.ReadAsync(created, token)).IsNull();
        await Assert.That(async () => await session.ExecuteAsync(Scenario.DocumentUpdate, created, token)).Throws<ComparisonFailureException>();
    }

    private static async Task RequireCreateConflictAsync(IComparisonSession session, BenchmarkDocument document, CancellationToken token)
    {
        string? failure = null;
        try
        {
            await session.ExecuteAsync(Scenario.DocumentWrite, document, token);
        }
        catch (ComparisonFailureException error)
        {
            failure = error.Message;
        }
        await Assert.That(failure).IsEqualTo("DocumentCreateConflict");
    }
}
