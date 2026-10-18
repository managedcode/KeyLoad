using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionClusterWave
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SqlParserAdmissionClusterState state, CancellationToken token)
    {
        await ScriptDomNamedParameterClusterReads.RunAsync(root, reader, state, token);
        await ScriptDomNamedParameterClusterWrites.RunAsync(root, state, token);
        await SqlParserAdmissionClusterUnicode.RunAsync(root, reader, state, token);
        await SqlParserAdmissionClusterNumericControls.RunAsync(reader, state, token);
        foreach (var (sql, code, detail) in SqlParserAdmissionClusterInputs.Corpus())
        {
            await SqlParserAdmissionClusterCurrent.ObserveAsync(root, state,
                () => SqlParserAdmissionClusterRefusals.QueryAsync(reader, state, sql, code, detail, code == ErrorCode.UnsupportedCapability, token), token);
            await SqlParserAdmissionClusterReads.HealthyAsync(reader, state, token);
        }
        await SqlParserAdmissionClusterCurrent.ObserveAsync(root, state,
            () => SqlParserAdmissionClusterRefusals.InsertAsync(root, state, token), token);
        await SqlParserAdmissionClusterCurrent.ObserveAsync(root, state,
            () => SqlParserAdmissionClusterCancellation.RunAsync(reader, state), token);
        await SqlParserAdmissionClusterReads.HealthyAsync(reader, state, token);
        await SqlParserAdmissionClusterCurrent.CheckAsync(root, state, token);
    }
    internal static async Task WarmAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SqlParserAdmissionClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await RunAsync(root, reader, state, token);
            await SqlParserAdmissionClusterSetup.PolicyAsync(root, state, 4L, false, token);
            foreach (var sql in new[] { SqlParserAdmissionClusterReads.Plain,
                "SELECT TOP (10) payload.value AS value FROM EVENTS('parser-events','source',+1) ORDER BY revision",
                "SELECT TOP (1) amount FROM [parser-typed] ORDER BY id" })
            {
                await SqlParserAdmissionClusterCurrent.ObserveAsync(root, state,
                    () => SqlParserAdmissionClusterRefusals.QueryAsync(reader, state, sql, ErrorCode.PermissionDenied,
                        "The principal cannot perform this operation in this scope.", true, token), token);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlParserAdmissionClusterSetup.PolicyAsync(root, state, 5L, true, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SqlParserAdmissionClusterReads.HealthyAsync(reader, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
