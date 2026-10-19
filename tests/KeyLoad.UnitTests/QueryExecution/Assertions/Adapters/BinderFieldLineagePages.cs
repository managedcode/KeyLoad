using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.QueryExecution.BinderFieldLineageProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class BinderFieldLineagePages
{
    private const string LowerExpression = "LOWER(d.[odd.name])";
    private const string CastExpression = "CAST(d.[odd.name] AS int)";
    private const string CoalesceExpression = "COALESCE(d.[odd.name], d.status)";
    private const string SelectPrefix = "SELECT ";
    private const string UnsupportedTail = " AS public_value FROM orders d";
    internal static async Task ControlsAsync(QueryEngine engine, ZoneTreeStore store,
        PartitionRef partition, CancellationToken token)
    {
        foreach (var mode in new[] { None, ReadOnly, UseOnly, Both })
        {
            await ModeAsync(engine, store, partition, ReaderPrefix + mode, mode, token);
        }
    }

    internal static async Task ModeAsync(QueryEngine engine, ZoneTreeStore store,
        PartitionRef partition, string subject, int mode, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            foreach (var shape in new[] { ProjectionOnly, PredicateShape, SortShape })
            {
                var ast = BinderFieldLineageRequests.Ast(partition, shape);
                Func<QueryPage>[] calls =
                [() => engine.Execute(subject, new(partition, BinderFieldLineageRequests.Sql(shape),
                        AllowFullScan: true), cancellationToken: token),
                 () => engine.ExecuteAst(subject, ast, cancellationToken: token),
                 () => engine.ExecuteAst(subject, Q2CapabilityFixtures.JsonRoundTrip(ast), cancellationToken: token),
                 () => engine.ExecuteAst(subject, Q2CapabilityFixtures.RuntimeRoundTrip(ast), cancellationToken: token)];
                foreach (var call in calls)
                {
                    if (shape != ProjectionOnly && (mode & UseOnly) == None)
                    {
                        await RefusalAsync(call, ErrorCode.PermissionDenied,
                            UseDenied);
                    }
                    else
                    {
                        await EqualAsync(BinderFieldLineageRequests.Expected(cut, (mode & ReadOnly) != None), call());
                    }
                }
            }
            await UnsupportedAsync(engine, partition, subject, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => TeamRowPolicyAssertions.UnchangedAsync(store, image, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task UnsupportedAsync(QueryEngine engine, PartitionRef partition,
        string subject, CancellationToken token)
    {
        foreach (var expression in new[] { LowerExpression, CastExpression, CoalesceExpression })
        {
            await RefusalAsync(() => engine.Execute(subject,
                new(partition, SelectPrefix + expression + UnsupportedTail, AllowFullScan: true),
                cancellationToken: token), ErrorCode.UnsupportedCapability,
                UnsupportedDetail);
        }
    }

    internal static async Task RevokedAsync(QueryEngine engine, ZoneTreeStore store,
        PartitionRef partition, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            foreach (var shape in new[] { ProjectionOnly, PredicateShape, SortShape })
            {
                await RefusalAsync(() => engine.Execute(ChangingSubject,
                    new(partition, BinderFieldLineageRequests.Sql(shape), AllowFullScan: true),
                    cancellationToken: token), ErrorCode.Unauthenticated, null);
                await RefusalAsync(() => engine.ExecuteAst(ChangingSubject,
                    BinderFieldLineageRequests.Ast(partition, shape), cancellationToken: token),
                    ErrorCode.Unauthenticated, null);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => TeamRowPolicyAssertions.UnchangedAsync(store, image, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefusalAsync(Func<QueryPage> execute, ErrorCode code, string? detail)
    {
        QueryPage? page = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => page = execute());
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(error.Message).DoesNotContain(Canary);
        if (detail is not null)
        {
            await Assert.That(error.Message).IsEqualTo(detail);
        }
        await Assert.That(page).IsNull();
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
