using KeyLoad.Query;
using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextAsyncProjectionTests
{
    private const string Collection = "native-text-async";
    private const string TextPath = "/text";
    private const string InitialText = "needle";
    private const string ReplacementText = "fresh";
    private const string FirstId = "one";
    private const string SecondId = "two";

    [Test]
    public async Task AcFts007AsyncNativeSearchPublishesReusesAndReplacesFromCanonicalCuts()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, FirstId, "{\"text\":\"needle\"}"),
            new PutDocument(Collection, SecondId, "{\"text\":\"other\"}"));
        using var projection = CreateProjection(database);
        var native = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var canonical = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(database.Partition, Collection, TextPath, InitialText);
        var token = TestContext.Current!.Execution.CancellationToken;

        var expectedInitial = await canonical.SearchAsync("root", request, token);
        var actualInitial = await native.SearchAsync("root", request, token);
        var firstGeneration = CurrentGeneration(database);
        var repeated = await native.SearchAsync("root", request, token);

        await NativeTextAsyncAssertions.AssertEquivalentAsync(expectedInitial, actualInitial);
        await NativeTextAsyncAssertions.AssertEquivalentAsync(expectedInitial, repeated);
        await Assert.That(actualInitial).HasSingleItem();
        await Assert.That(actualInitial[0].Document.Reference.Id).IsEqualTo(FirstId);
        await Assert.That(CurrentGeneration(database)).IsEqualTo(firstGeneration);

        var priorPosition = database.Store.Position;
        database.Commit(new PutDocument(Collection, FirstId, "{\"text\":\"fresh\"}", ExpectedRevision: 1));
        await Assert.That(database.Store.Position).IsGreaterThan(priorPosition);
        var expectedOldTerm = await canonical.SearchAsync("root", request, token);
        var actualOldTerm = await native.SearchAsync("root", request, token);
        var updatedGeneration = CurrentGeneration(database);
        await Assert.That(updatedGeneration).IsNotEqualTo(firstGeneration);
        var replacementRequest = request with { Text = ReplacementText };
        var expectedReplacement = await canonical.SearchAsync("root", replacementRequest, token);
        var actualReplacement = await native.SearchAsync("root", replacementRequest, token);

        await NativeTextAsyncAssertions.AssertEquivalentAsync(expectedOldTerm, actualOldTerm);
        await NativeTextAsyncAssertions.AssertEquivalentAsync(expectedReplacement, actualReplacement);
        await Assert.That(actualOldTerm).IsEmpty();
        await Assert.That(actualReplacement).HasSingleItem();
        await Assert.That(actualReplacement[0].Document.Reference.Id).IsEqualTo(FirstId);
        await Assert.That(CurrentGeneration(database)).IsEqualTo(updatedGeneration);
        await Assert.That(GenerationCount(database)).IsEqualTo(1);
    }

    private static NativeTextProjection CreateProjection(TestDatabase database)
        => new(Path.Combine(database.Directory, "native-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());

    private static int GenerationCount(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "native-text"))
            .Count(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal));

    private static string CurrentGeneration(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "native-text"))
            .Single(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal));
}

internal static class NativeTextAsyncAssertions
{
    internal static async Task AssertEquivalentAsync(RankedDocument[] expected, RankedDocument[] actual)
    {
        await Assert.That(actual.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(expected.Select(result => result.Document.Reference.Id).ToArray(), CollectionOrdering.Matching);
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Document).IsEquivalentTo(expected[index].Document);
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score);
        }
    }
}
