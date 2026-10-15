using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionRf3Wave
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SqlParserAdmissionRf3State state, CancellationToken token)
    {
        await SqlParserAdmissionRf3Unicode.RunAsync(root, reader, state, token);
        await SqlParserAdmissionRf3NumericControls.RunAsync(reader, state, token);
        foreach (var (sql, code, detail) in SqlParserAdmissionRf3Inputs.Corpus())
        {
            await SqlParserAdmissionRf3Current.ObserveAsync(root, state,
                () => SqlParserAdmissionRf3Refusals.QueryAsync(reader, state, sql, code, detail, code == ErrorCode.UnsupportedCapability, token), token);
            await SqlParserAdmissionRf3Reads.HealthyAsync(reader, state, token);
        }
        await SqlParserAdmissionRf3Current.ObserveAsync(root, state,
            () => SqlParserAdmissionRf3Refusals.InsertAsync(root, state, token), token);
        await SqlParserAdmissionRf3Current.ObserveAsync(root, state,
            () => SqlParserAdmissionRf3Cancellation.RunAsync(reader, state), token);
        await SqlParserAdmissionRf3Reads.HealthyAsync(reader, state, token);
        await SqlParserAdmissionRf3Current.CheckAsync(root, state, token);
    }
    internal static async Task WarmAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SqlParserAdmissionRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await RunAsync(root, reader, state, token);
            await SqlParserAdmissionRf3Setup.PolicyAsync(root, state, 4L, false, token);
            foreach (var sql in new[] { SqlParserAdmissionRf3Reads.Plain,
                "SELECT TOP (10) payload.value AS value FROM EVENTS('parser-events','source',+1) ORDER BY revision",
                "SELECT TOP (1) amount FROM [parser-typed] ORDER BY id" })
            {
                await SqlParserAdmissionRf3Current.ObserveAsync(root, state,
                    () => SqlParserAdmissionRf3Refusals.QueryAsync(reader, state, sql, ErrorCode.PermissionDenied,
                        "The principal cannot perform this operation in this scope.", true, token), token);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlParserAdmissionRf3Setup.PolicyAsync(root, state, 5L, true, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SqlParserAdmissionRf3Reads.HealthyAsync(reader, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
