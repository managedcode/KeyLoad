using KeyLoad.Core.Search;
using KeyLoad.Query;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextProjectionParityTests
{
    private const string Collection = "execution-text-parity";
    private const string TextPath = "/text";
    private const string VectorPath = "/embedding";
    private const string Query = "CAFÉ 𐐨 𐐪 123 x repeated";
    private const string PooledDocument = "pooled-word";
    private static readonly string LongToken = new('A', 129);
    private static readonly VectorSpace Space = new("execution-text-parity", 2, DistanceMetric.DotProduct, "test", "1");

    [Test]
    public async Task GenerationPreservesUnicodeCorpusScoresTiesAndHybridRanks()
    {
        using var database = new TestDatabase();
        SeedCorpus(database);
        using var projection = CreateProjection(database);
        var oracle = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var execution = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var token = TestContext.Current!.Execution.CancellationToken;
        var textRequest = new SearchRequest(database.Partition, Collection, TextPath,
            Query + " " + new string('a', LongToken.Length), Limit: 20);
        var hybridRequest = textRequest with
        {
            VectorField = VectorPath,
            Vector = [1, 0],
            Space = Space,
            Limit = 3
        };

        var expectedText = await oracle.SearchAsync("root", textRequest, token);
        var expectedHybrid = await oracle.SearchAsync("root", hybridRequest, token);
        var actualText = await execution.SearchAsync("root", textRequest, token);
        var firstGeneration = CurrentGeneration(database);
        var actualHybrid = await execution.SearchAsync("root", hybridRequest, token);

        await AssertEquivalentAsync(expectedText, actualText);
        await AssertEquivalentAsync(expectedHybrid, actualHybrid);
        await AssertEquivalentAsync(expectedText, await execution.SearchAsync("root", textRequest, token));
        await AssertTokenizerBoundsAsync(token);
        await Assert.That(CurrentGeneration(database)).IsEqualTo(firstGeneration);
        await Assert.That(actualText.Select(result => result.Document.Reference.Id)).Contains("decomposed");
        await Assert.That(actualText.Select(result => result.Document.Reference.Id)).Contains("supplementary");
        await Assert.That(actualText.Select(result => result.Document.Reference.Id)).Contains("digits-short-repeat");
        await Assert.That(actualText.Select(result => result.Document.Reference.Id)).Contains(PooledDocument);
        await Assert.That(actualText.Any(result => result.Document.Reference.Id == "missing-text")).IsFalse();
        await Assert.That(actualText.Any(result => result.Document.Reference.Id == "empty-text")).IsFalse();
        var identities = actualText.Select(result => result.Document.Reference.Id).ToArray();
        var leftTie = Array.IndexOf(identities, "tie-left");
        await Assert.That(leftTie).IsGreaterThanOrEqualTo(0);
        await Assert.That(Array.IndexOf(identities, "tie-right")).IsEqualTo(leftTie + 1);
        await Assert.That(actualHybrid.Length).IsEqualTo(3);
    }

    [Test]
    [Arguments(0UL)]
    [Arguments(1UL)]
    public async Task DeliberateTokenHashCollisionsCannotChangeCanonicalResults(ulong tokenHash)
    {
        using var database = new TestDatabase();
        SeedCorpus(database);
        var currentHash = tokenHash;
        using var projection = CreateProjection(database, _ => currentHash);
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query, Limit: 20);
        var token = TestContext.Current!.Execution.CancellationToken;
        var oracle = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var expected = await oracle.SearchAsync("root", request, token);
        await AssertEquivalentAsync(expected, await search.SearchAsync("root", request, token));
        await AssertEquivalentAsync(expected, await search.SearchAsync("root", request, token));

        currentHash = tokenHash + 1;
        var missingPositive = Assert.ThrowsExactly<KeyLoadException>(() => search.Search("root", request, token));
        await Assert.That(missingPositive.Code).IsEqualTo(ErrorCode.Corruption);
        currentHash = tokenHash;
        await AssertEquivalentAsync(expected, await search.SearchAsync("root", request, token));
    }

    private static void SeedCorpus(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(
            new PutDocument(Collection, "decomposed", "{\"text\":\"Café café\"}"),
            new PutDocument(Collection, "supplementary", "{\"text\":\"𐐀𐐨 𐐪\"}"),
            new PutDocument(Collection, "digits-short-repeat", "{\"text\":\"123 x repeated repeated repeated\"}"),
            new PutDocument(Collection, "tie-left", "{\"text\":\"café\"}"),
            new PutDocument(Collection, "tie-right", "{\"text\":\"CAFÉ\"}"),
            new PutDocument(Collection, "empty-text", "{\"text\":\"\"}"),
            new PutDocument(Collection, PooledDocument, "{\"text\":\"" + LongToken + "!tail\"}"),
            new PutDocument(Collection, "missing-text", "{\"other\":\"café 123 repeated\"}"));
        database.Commit(
            new PutVector(Collection, "decomposed", VectorPath, [1, 0], Space, 1),
            new PutVector(Collection, "supplementary", VectorPath, [0, 1], Space, 1),
            new PutVector(Collection, "digits-short-repeat", VectorPath, [1, 0], Space, 1),
            new PutVector(Collection, "tie-left", VectorPath, [0.5f, 0], Space, 1),
            new PutVector(Collection, "tie-right", VectorPath, [0.5f, 0], Space, 1));
    }

    private static async Task AssertTokenizerBoundsAsync(CancellationToken token)
    {
        const int MaximumWords = 2;
        var work = new ReadExecutionBudget(cancellationToken: token);
        await Assert.That(ReadTerms(LongToken + "!𐐀", work, MaximumWords, LongToken.Length))
            .IsEquivalentTo(new[] { new string('a', LongToken.Length), "𐐨" }, CollectionOrdering.Matching);
        var tooLong = Assert.ThrowsExactly<KeyLoadException>(() => ReadTerms(LongToken + "a", work, MaximumWords, LongToken.Length));
        await Assert.That(tooLong.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var tooMany = Assert.ThrowsExactly<KeyLoadException>(() => ReadTerms(LongToken + " x y", work, MaximumWords, LongToken.Length));
        await Assert.That(tooMany.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var surrogate = Assert.ThrowsExactly<KeyLoadException>(() => ReadTerms("𐐀", work, MaximumWords, 1));
        await Assert.That(surrogate.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static string[] ReadTerms(string text, ReadExecutionBudget work, int maximumWords, int maximumWordLength)
    {
        const int CheckEachRune = 1;
        Span<char> word = stackalloc char[CanonicalTextTerms.InlineWordCapacity];
        var result = new List<string>();
        using var tokens = new CanonicalTextTerms(text, word, work, CheckEachRune, maximumWords, maximumWordLength);
        while (tokens.MoveNext())
        {
            result.Add(tokens.Current.ToString());
        }
        return [.. result];
    }

    private static RuntimeTextProjection CreateProjection(TestDatabase database, Func<string, ulong>? tokenHash = null)
        => new(Path.Combine(database.Directory, "execution-text"),
            database.Store.Identity.NodeId, UnitTextOptions.Execution(), new TextResourceOwnership(UnitTextOptions.Execution()), tokenHash);

    private static string CurrentGeneration(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "execution-text"))
            .Single(path => Path.GetFileName(path).StartsWith(TextProtocol.GenerationPrefix,
                StringComparison.Ordinal));

    private static async Task AssertEquivalentAsync(RankedDocument[] expected, RankedDocument[] actual)
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
