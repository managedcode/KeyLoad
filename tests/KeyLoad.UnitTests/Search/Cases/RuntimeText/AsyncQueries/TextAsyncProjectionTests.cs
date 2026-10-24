using KeyLoad.Query;
using KeyLoad.Server.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextAsyncProjectionTests
{
    private const string Collection = "execution-text-async";
    private const string TextPath = "/text";
    private const string InitialText = "needle";
    private const string ReplacementText = "fresh";
    private const string FirstId = "one";
    private const string SecondId = "two";

    [Test]
    public async Task AcFts007AsyncSearchPublishesReusesAndReplacesFromCanonicalCuts()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, FirstId, "{\"text\":\"needle\"}"),
            new PutDocument(Collection, SecondId, "{\"text\":\"other\"}"));
        using var projection = CreateProjection(database);
        var execution = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var canonical = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(database.Partition, Collection, TextPath, InitialText);
        var token = TestContext.Current!.Execution.CancellationToken;

        var expectedInitial = await canonical.SearchAsync("root", request, token);
        var actualInitial = await execution.SearchAsync("root", request, token);
        var firstGeneration = CurrentGeneration(database);
        var repeated = await execution.SearchAsync("root", request, token);

        await TextAsyncAssertions.AssertEquivalentAsync(expectedInitial, actualInitial);
        await TextAsyncAssertions.AssertEquivalentAsync(expectedInitial, repeated);
        await Assert.That(actualInitial).HasSingleItem();
        await Assert.That(actualInitial[0].Document.Reference.Id).IsEqualTo(FirstId);
        await Assert.That(CurrentGeneration(database)).IsEqualTo(firstGeneration);

        var priorPosition = database.Store.Position;
        database.Commit(new PutDocument(Collection, FirstId, "{\"text\":\"fresh\"}", ExpectedRevision: 1));
        await Assert.That(database.Store.Position).IsGreaterThan(priorPosition);
        var expectedOldTerm = await canonical.SearchAsync("root", request, token);
        var actualOldTerm = await execution.SearchAsync("root", request, token);
        var updatedGeneration = CurrentGeneration(database);
        await Assert.That(updatedGeneration).IsNotEqualTo(firstGeneration);
        var replacementRequest = request with
        {
            Text = ReplacementText
        };
        var expectedReplacement = await canonical.SearchAsync("root", replacementRequest, token);
        var actualReplacement = await execution.SearchAsync("root", replacementRequest, token);

        await TextAsyncAssertions.AssertEquivalentAsync(expectedOldTerm, actualOldTerm);
        await TextAsyncAssertions.AssertEquivalentAsync(expectedReplacement, actualReplacement);
        await Assert.That(actualOldTerm).IsEmpty();
        await Assert.That(actualReplacement).HasSingleItem();
        await Assert.That(actualReplacement[0].Document.Reference.Id).IsEqualTo(FirstId);
        await Assert.That(CurrentGeneration(database)).IsEqualTo(updatedGeneration);
        await Assert.That(GenerationCount(database)).IsEqualTo(1);
    }

    private static RuntimeTextProjection CreateProjection(TestDatabase database)
        => new(Path.Combine(database.Directory, "execution-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitTextOptions.Execution());

    private static int GenerationCount(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "execution-text"))
            .Count(path => Path.GetFileName(path).StartsWith(TextProtocol.GenerationPrefix,
                StringComparison.Ordinal));

    private static string CurrentGeneration(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "execution-text"))
            .Single(path => Path.GetFileName(path).StartsWith(TextProtocol.GenerationPrefix,
                StringComparison.Ordinal));
}
