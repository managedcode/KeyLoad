namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-SQLC-002/004: trivia boundaries and bounded scalar progress.</summary>
internal sealed class SqlTriviaReaderTests
{
    private const string Nested = " \r\n-- ignored ; CALL\r\n/* outer /* inner */ tail */SELECT";
    private const string Select = "SELECT";
    private const string Unclosed = "/* unfinished";
    private const string LineAtEnd = "-- unfinished line";
    private const string Deep = "/* /* */ */SELECT";
    private const string Quoted = "'/* -- */'";
    private const string Gap = "SEL/* gap */ECT";
    private const int DepthLimit = 2;
    private const int ShallowDepth = 1;
    private const int LongCommentLength = 4_096;
    private const int OriginalChunkCharacters = 256;
    private const char CommentCharacter = 'x';
    private const string OpenComment = "/*";
    private const string CloseComment = "*/";
    private const char Whitespace = ' ';

    [Test]
    public async Task AC_SQLC_002_CommentsStopAtTheOriginalFirstToken()
    {
        await OwnedAsync(async database =>
        {
            await SeedAsync(database);
            var prefix = Nested[..^Select.Length];
            var tokens = Tokens(prefix + ScriptDomSelectPages.WindowSql, DepthLimit);
            await Assert.That(tokens.First(token => token.Text == Select).Offset).IsEqualTo(prefix.Length);
            var quoted = Tokens(Quoted, DepthLimit);
            await Assert.That(quoted[0].Offset).IsEqualTo(0);
            await Assert.That(quoted[0].Text).IsEqualTo(Quoted);
            var gap = Tokens(Gap, DepthLimit);
            await Assert.That(gap[0].Offset).IsEqualTo(0);
            await Assert.That(gap[0].Text).IsEqualTo("SEL");
            await HealthyAsync(database, prefix + ScriptDomSelectPages.WindowSql);
            await QuotedAsync(database);
            await RefuseAsync(database, Gap + " * FROM offsetleft", ErrorCode.Validation);
        });
    }

    [Test]
    public async Task AC_SQLC_002_UnclosedBlocksAndNestedDepthAreDistinct()
    {
        await OwnedAsync(async database =>
        {
            await SeedAsync(database);
            await RefuseAsync(database, Unclosed + ScriptDomSelectPages.WindowSql, ErrorCode.Validation);
            await HealthyAsync(database, ScriptDomSelectPages.WindowSql + " " + LineAtEnd);
            var deep = Deep[..^Select.Length] + ScriptDomSelectPages.WindowSql;
            var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
            var position = database.Store.Position;
            var identity = JsonDefaults.Serialize(database.Store.Identity);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => Tokens(deep, ShallowDepth));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            var tokens = Tokens(deep, DepthLimit);
            await Assert.That(tokens.First(token => token.Text == Select).Offset).IsEqualTo(Deep.Length - Select.Length);
            await HealthyAsync(database, deep);
        });
    }

    [Test]
    public async Task AC_SQLC_004_EveryChunkHasBoundedPositiveProgress()
    {
        await OwnedAsync(async database =>
        {
            await SeedAsync(database);
            var sql = OpenComment + new string(CommentCharacter, LongCommentLength) + CloseComment + ScriptDomSelectPages.WindowSql;
            await CopyAsync(sql);
            var tokens = Tokens(sql, DepthLimit);
            await Assert.That(tokens.First(token => token.Text == Select).Offset)
                .IsEqualTo(OpenComment.Length + LongCommentLength + CloseComment.Length);
            await HealthyAsync(database, sql);
        });
    }

    [Test]
    public async Task AC_SQLC_004_CommentDelimitersAcrossChunkBoundariesRemainTrivia()
    {
        await OwnedAsync(async database =>
        {
            await SeedAsync(database);
            var prefix = new string(Whitespace, OriginalChunkCharacters - 1);
            var sql = prefix + OpenComment + new string(CommentCharacter, LongCommentLength) + CloseComment + ScriptDomSelectPages.WindowSql;
            await CopyAsync(sql);
            var tokens = Tokens(sql, DepthLimit);
            await Assert.That(tokens.First(token => token.Text == Select).Offset)
                .IsEqualTo(prefix.Length + OpenComment.Length + LongCommentLength + CloseComment.Length);
            await HealthyAsync(database, sql);
        });
    }

    private static async Task OwnedAsync(Func<TestDatabase, Task> operation)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => operation(database), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SeedAsync(TestDatabase database)
    {
        SqlOffsetFixture.Seed(database);
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.GrantEpoch, true);
    }

    private static IList<global::Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken> Tokens(string sql, int depth)
    {
        var options = UnitExecutionOptions.DatabaseLimits(new DatabaseLimits { MaxQueryDepth = depth });
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(options);
        using var reader = new global::KeyLoad.Features.QueryExecution.ScriptDomBoundedReader(sql, budget.Check);
        var parser = new global::Microsoft.SqlServer.TransactSql.ScriptDom.TSql180Parser(true,
            global::Microsoft.SqlServer.TransactSql.ScriptDom.SqlEngineType.Standalone);
        var tokens = parser.GetTokenStream(reader, out var errors);
        if (errors.Count != 0)
        { throw new InvalidOperationException("The native token fixture is malformed."); }
        global::KeyLoad.Features.QueryExecution.ScriptDomTokenBounds.Require(tokens, options.Value, budget.Check);
        return tokens;
    }

    private static async Task CopyAsync(string sql)
    {
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits());
        using var reader = new global::KeyLoad.Features.QueryExecution.ScriptDomBoundedReader(sql, budget.Check);
        var copied = new global::System.Text.StringBuilder();
        var buffer = new char[OriginalChunkCharacters];
        while (await reader.ReadAsync(buffer.AsMemory(), TestContext.Current!.Execution.CancellationToken) is var count && count > 0)
        {
            await Assert.That(count).IsGreaterThan(0);
            await Assert.That(count).IsLessThanOrEqualTo(OriginalChunkCharacters);
            copied.Append(buffer, 0, count);
        }
        await Assert.That(copied.ToString()).IsEqualTo(sql);
    }

    private static async Task HealthyAsync(TestDatabase database, string sql)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var page = engine.Execute(SqlOffsetProtocol.Reader, new(database.Partition, sql, AllowFullScan: true));
        await ScriptDomSelectPages.PageAsync(page, "b", "{\"title\":\"second\",\"order_id\":\"b\"}", position, false);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }

    private static async Task QuotedAsync(TestDatabase database)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var sql = "SELECT title, order_id FROM offsetleft WHERE title = " + Quoted + " ORDER BY order_id";
        var actual = engine.Execute(SqlOffsetProtocol.Reader, new(database.Partition, sql, AllowFullScan: true));
        var expected = new QueryPage([], null, position, ScriptDomSelectPages.FullScan);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }

    private static async Task RefuseAsync(TestDatabase database, string sql, ErrorCode code)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlOffsetProtocol.Reader,
            new(database.Partition, sql, AllowFullScan: true)));
        await Assert.That(failure.Code).IsEqualTo(code);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
}
