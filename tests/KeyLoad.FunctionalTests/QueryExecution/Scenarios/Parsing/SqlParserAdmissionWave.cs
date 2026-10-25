using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlParserAdmissionWave
{
    internal static async Task RunAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
    {
        await ScriptDomOrdinalReadAdmission.RunAsync(fixture, database, store, token);
        await SqlParserAdmissionUnicode.RunAsync(fixture, database, store, token);
        await SqlParserAdmissionNumericControls.RunAsync(fixture, database, store, token);
        await SqlParserAdmissionModelPages.InsertRefusalAsync(fixture, store, token);
        foreach (var (sql, code, detail) in SqlParserAdmissionInputs.Corpus())
        {
            await SqlParserAdmissionReads.RefuseAsync(fixture, database, store, sql, code, detail, token);
            await SqlParserAdmissionReads.HealthyAsync(fixture, database, store, token);
        }
    }
}
