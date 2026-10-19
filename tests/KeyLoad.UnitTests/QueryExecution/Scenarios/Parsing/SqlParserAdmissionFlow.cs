using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlParserAdmissionFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SqlParserAdmissionFixture();
            await ServerFailureObserver.ObserveAsync(() => BodyAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task BodyAsync(SqlParserAdmissionFixture fixture, CancellationToken token)
    {
        await fixture.InitializeAsync();
        var db = fixture.Harness;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlParserAdmissionWave.RunAsync(fixture, db.Database, db.Store, token);
            await ScriptDomSelectCancellation.RequireAsync(db, new(db.Database, UnitExecutionOptions.QueryExecution()));
            await ScriptDomParsingCancellation.RequireAsync(db, new(db.Database, UnitExecutionOptions.QueryExecution()));
            await fixture.PolicyAsync(2L, false);
            foreach (var sql in new[] { "SELECT TOP (1) title FROM offsetleft ORDER BY order_id",
                "SELECT TOP (10) payload.value AS value FROM EVENTS('parser-events','source',+1) ORDER BY revision",
                "SELECT TOP (1) amount FROM [parser-typed] ORDER BY id" })
            {
                await SqlParserAdmissionReads.RefuseAsync(fixture, db.Database, db.Store, sql, ErrorCode.PermissionDenied,
                    "The principal cannot perform this operation in this scope.", token);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => fixture.PolicyAsync(3L, true), failures);
        await ServerFailureObserver.ObserveAsync(() => SqlParserAdmissionReads.HealthyAsync(fixture, db.Database, db.Store, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SqlParserAdmissionCold.RunAsync(fixture, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
