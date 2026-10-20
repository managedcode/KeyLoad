using System.Collections.Immutable;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.QueryExecution.BinderFieldLineageProtocol;
using P = KeyLoad.UnitTests.Features.QueryExecution.SchemaVisibleProjectionProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SchemaVisibleProjectionAssertions
{
    private const string SafeJson = "{\"people\":[{\"name\":\"A\"}],\"status\":\"open\"}";

    internal static async Task WaveAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition,
        bool raw, bool visible, bool mixed, CancellationToken token)
    {
        await ObserveAsync(store, async () =>
        {
            var paths = raw ? ImmutableArray<string>.Empty : mixed ? [ArrayPolicy] : ImmutableArray<string>.Empty;
            await PageAsync(engine.ExecuteAst(P.Reader, SchemaVisibleProjectionRequests.Star(partition),
                cancellationToken: token), store.Position, raw ? BinderFieldLineageRequests.OriginalJson : SafeJson, !raw, paths);
            if (visible)
            {
                await SelectedAsync(engine, store, partition, token);
            }
            else
            {
                foreach (var request in SchemaVisibleProjectionRequests.Restricted(partition)
                    .Concat(SchemaVisibleProjectionJoins.Requests(partition)))
                {
                    await ObserveAsync(store, () => RefusalAsync(() => engine.Execute(P.Reader, request.Sql, cancellationToken: token), request.Sql.Sql));
                    await ObserveAsync(store, () => RefusalAsync(() => engine.ExecuteAst(P.Reader, request.Ast, cancellationToken: token)));
                }
                var star = SchemaVisibleProjectionRequests.Star(partition);
                await ObserveAsync(store, () => RefusalAsync(() => engine.ExecuteAst(P.Reader, star with
                {
                    Query = star.Query with
                    {
                        Explain = true
                    }
                }, cancellationToken: token)));
            }
            var expected = new[] { P.IdJson, P.RevisionJson };
            var index = P.First;
            foreach (var request in SchemaVisibleProjectionRequests.System(partition))
            {
                await PageAsync(engine.ExecuteAst(P.Reader, request, cancellationToken: token), store.Position,
                    expected[index++], false, []);
            }
        });
    }

    private static async Task SelectedAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition, CancellationToken token)
    {
        foreach (var protectedPath in new[] { false, true })
        {
            var request = SchemaVisibleProjectionRequests.Selected(partition, protectedPath);
            var json = protectedPath ? P.ProtectedJson : P.PublicJson;
            await PageAsync(engine.Execute(P.Reader, request.Sql, cancellationToken: token), store.Position, json, false, []);
            await PageAsync(engine.ExecuteAst(P.Reader, request.Ast, cancellationToken: token), store.Position, json, false, []);
        }
    }

    internal static async Task RefusalAsync(Func<QueryPage> operation, string context = "Typed query")
    {
        QueryPage? actual = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => actual = operation());
        await Assert.That(actual is null).IsTrue();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(P.Detail).Because(context);
        await Assert.That(error.StatusCode).IsEqualTo(Errors.Status(ErrorCode.Validation));
        await Assert.That(error.InnerException is null).IsTrue();
        await Assert.That(error.Message.Contains(Canary, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task ObserveAsync(ZoneTreeStore store, Func<Task> operation)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var position = store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => TeamRowPolicyAssertions.UnchangedAsync(store, image, position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task PageAsync(QueryPage actual, long cut, string json, bool redacted, ImmutableArray<string> paths)
        => BinderFieldLineagePages.EqualAsync(new QueryPage([new(DocumentId, OriginalRevision, json, redacted, paths)],
            null, cut, P.Scan), actual);
}
