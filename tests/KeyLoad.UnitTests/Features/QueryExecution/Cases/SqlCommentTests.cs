using System.Text;
using System.Text.Json;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlCommentTests
{
    [Test]
    public async Task AcSqlc002_CommentsPreserveRealSelectRowsAndExplainAccessPath()
    {
        using var db = new TestDatabase();
        db.Configure(SqlCommentTestData.Collection, ResourceKind.Collection,
            indexes: [new(SqlCommentTestData.Status, [SqlCommentTestData.StatusPath])]);
        db.Commit(new PutDocument(SqlCommentTestData.Collection, SqlCommentTestData.DocumentA,
                SqlCommentTestData.OpenDocumentJson),
            new PutDocument(SqlCommentTestData.Collection, SqlCommentTestData.DocumentB,
                SqlCommentTestData.ClosedDocumentJson));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());

        var plain = engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.PlainSelect));
        var commented = engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.CommentedSelect));
        var eofCommented = engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.EndOfInputLineCommentSelect));
        await Assert.That(JsonDefaults.Serialize(plain.Rows).SequenceEqual(JsonDefaults.Serialize(commented.Rows))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(plain.Rows).SequenceEqual(JsonDefaults.Serialize(eofCommented.Rows))).IsTrue();
        await Assert.That(commented.Rows.Select(row => row.EntityId)).IsEquivalentTo(
            [SqlCommentTestData.DocumentA], CollectionOrdering.Matching);
        await Assert.That(commented.AccessPath).IsEqualTo(SqlCommentTestData.IndexPath);

        var plainExplain = engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.PlainExplain));
        var commentedExplain = engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.CommentedExplain));
        await Assert.That(JsonDefaults.Serialize(plainExplain.Rows).SequenceEqual(JsonDefaults.Serialize(commentedExplain.Rows))).IsTrue();
        await Assert.That(commentedExplain.AccessPath).IsEqualTo(plainExplain.AccessPath);
    }

    [Test]
    public async Task AcSqlc002_CommentLikeQuotedIdentifiersAndStringValuesRemainData()
    {
        using var db = new TestDatabase();
        db.Configure(SqlCommentTestData.Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(SqlCommentTestData.Collection, SqlCommentTestData.DocumentA,
            SqlCommentTestData.QuotedDocumentJson));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());

        var result = engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.QuotedSql, AllowFullScan: true));

        await Assert.That(result.Rows).HasSingleItem();
        using var json = JsonDocument.Parse(result.Rows[0].Json);
        await Assert.That(json.RootElement.GetProperty(SqlCommentTestData.QuotedField).GetString())
            .IsEqualTo(SqlCommentTestData.QuotedValue);
    }

    [Test]
    public async Task AcSqlc002_NumericLiteralCanEndImmediatelyBeforeLineComment()
    {
        using var db = new TestDatabase();
        db.Configure(SqlCommentTestData.Collection, ResourceKind.Collection,
            indexes: [new(SqlCommentTestData.NumberField, [SqlCommentTestData.NumberPath])]);
        db.Commit(new PutDocument(SqlCommentTestData.Collection, SqlCommentTestData.DocumentA,
            SqlCommentTestData.NumericDocumentPrefix + SqlCommentTestData.NumberValue
            + SqlCommentTestData.NumericDocumentSuffix));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());

        var result = engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.NumericLineCommentSql));

        await Assert.That(result.Rows.Select(row => row.EntityId)).IsEquivalentTo(
            [SqlCommentTestData.DocumentA], CollectionOrdering.Matching);
        await Assert.That(result.AccessPath).IsEqualTo(SqlCommentTestData.NumberIndexPath);
        await Assert.That(() => new SqlParser(SqlCommentTestData.NumericArithmeticLikeSql, new()).Parse())
            .Throws<KeyLoadException>();
    }

    [Test]
    public async Task AcSqlc002_UnclosedDeepSplicedAndSecondStatementCommentsAreRejected()
    {
        var limits = new DatabaseLimits { MaxQueryDepth = SqlCommentTestData.OverDepth };
        await Assert.That(new SqlParser(SqlCommentTestData.DeepComment,
            new() { MaxQueryDepth = SqlCommentTestData.MaximumCommentDepth }).Parse().Collection)
            .IsEqualTo(SqlCommentTestData.Collection);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser(
            SqlCommentTestData.UnclosedComment, new()).Parse()).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser(
            SqlCommentTestData.DeepComment, limits).Parse()).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(() => new SqlParser(SqlCommentTestData.IdentifierSplice, new()).Parse())
            .Throws<KeyLoadException>();
        await Assert.That(() => new SqlParser(SqlCommentTestData.OperatorSplice, new()).Parse())
            .Throws<KeyLoadException>();
        await Assert.That(() => new SqlParser(SqlCommentTestData.SecondStatement, new()).Parse())
            .Throws<KeyLoadException>();
    }

    [Test]
    public async Task AcSqlc002_CommentBytesAndTokensRemainInsideOriginalLimits()
    {
        var byteCount = Encoding.UTF8.GetByteCount(SqlCommentTestData.ExactBoundedQuery);
        var exactLimits = new DatabaseLimits { MaxQueryBytes = byteCount, MaxQueryTokens = SqlCommentTestData.ExactTokenCount };
        await Assert.That(new SqlParser(SqlCommentTestData.ExactBoundedQuery, exactLimits).Parse().Collection)
            .IsEqualTo(SqlCommentTestData.Collection);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => _ = new SqlParser(
            SqlCommentTestData.ExactBoundedQuery, exactLimits with { MaxQueryBytes = byteCount - 1 }).Parse()).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => _ = new SqlParser(
            SqlCommentTestData.TokenBudgetQuery, exactLimits with { MaxQueryTokens = SqlCommentTestData.ExcessTokenCount }).Parse()).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcSqlc004_PreCancelledQueryLeavesTheRealStoreReadable()
    {
        using var db = new TestDatabase();
        db.Configure(SqlCommentTestData.Collection, ResourceKind.Collection,
            indexes: [new(SqlCommentTestData.Status, [SqlCommentTestData.StatusPath])]);
        db.Commit(new PutDocument(SqlCommentTestData.Collection, SqlCommentTestData.DocumentA,
            SqlCommentTestData.OpenDocumentJson));
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        using var preCancelled = new CancellationTokenSource();
        await preCancelled.CancelAsync();
        await Assert.That(() => engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.PlainSelect), cancellationToken: preCancelled.Token))
            .Throws<OperationCanceledException>();
        var healthy = engine.Execute(SqlCommentTestData.Principal,
            new(db.Partition, SqlCommentTestData.PlainSelect));
        await Assert.That(healthy.Rows.Select(row => row.EntityId)).IsEquivalentTo(
            [SqlCommentTestData.DocumentA], CollectionOrdering.Matching);
    }
}
