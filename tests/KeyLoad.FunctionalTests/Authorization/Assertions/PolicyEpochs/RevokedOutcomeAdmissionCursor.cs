using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class RevokedOutcomeAdmissionCursor
{
    internal static async Task InitialAsync(RevokedOutcomeAdmissionFixture fixture, CancellationToken token)
    {
        var first = Page(fixture, null, token);
        await RowAsync(first, "a", 1, fixture.Store.Position);
        await Assert.That(first.Cursor).IsNotNull();
        fixture.Cursor = first.Cursor!;
        var claims = fixture.Database.Verify<QueryCursorClaims>(fixture.Cursor);
        await Assert.That(claims.PolicyEpoch).IsEqualTo(1L);
        await Assert.That(claims.ExpiresAt).IsGreaterThan(RevokedOutcomeAdmissionFixture.Epoch);
        await RowAsync(Page(fixture, fixture.Cursor, token), "b", 2, first.CutPosition);
    }
    internal static async Task FencedAsync(RevokedOutcomeAdmissionFixture fixture, CancellationToken token)
    {
        var claims = fixture.Database.Verify<QueryCursorClaims>(fixture.Cursor);
        await Assert.That(claims.ExpiresAt).IsGreaterThan(fixture.Database.EvaluationClock.GetUtcNow());
        await Assert.That(claims.PolicyEpoch).IsEqualTo(1L);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Page(fixture, fixture.Cursor, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.CursorExpired);
        await Assert.That(failure.Message).IsEqualTo("The query cursor no longer has a valid authorized read cut.");
    }
    internal static async Task HealthyAsync(RevokedOutcomeAdmissionFixture fixture, bool fresh, CancellationToken token)
    {
        var position = fixture.Store.Position;
        string? cursor = null;
        var count = fresh ? 3 : 2;
        for (var index = 0; index < count; index++)
        {
            var page = Page(fixture, cursor, token);
            string[] ids = ["a", "b", "c"];
            await RowAsync(page, ids[index], index + 1, position);
            await Assert.That(page.Cursor is null).IsEqualTo(index == count - 1);
            cursor = page.Cursor;
        }
    }
    private static QueryPage Page(RevokedOutcomeAdmissionFixture fixture, string? cursor, CancellationToken token)
        => new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).Execute(RevokedOutcomeAdmissionFixture.Worker,
            new(fixture.Harness.Partition, "SELECT TOP (1) * FROM " + RevokedOutcomeAdmissionFixture.Documents,
                Cursor: cursor, AllowFullScan: true), timeProvider: fixture.Database.EvaluationClock, cancellationToken: token);
    private static async Task RowAsync(QueryPage page, string id, int value, long position)
    {
        await RevokedOutcomeAdmissionValues.EqualAsync(page.Rows.ToArray(),
            new[] { new QueryRow(id, 1, "{\"value\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", false, []) });
        await Assert.That(page.AccessPath).IsEqualTo("bounded-full-scan");
        await Assert.That(page.CutPosition).IsEqualTo(position);
    }
}
