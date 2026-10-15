using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.QueryExecution.QueryEquivalentIntegerDecimalProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerDecimalPages
{
    internal static async Task RunAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition,
        ErrorCode? denied, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await MatrixAsync(engine, partition, denied, cut, token);
            var count = QueryEquivalentApplicationNumber.Calls;
            foreach (var call in QueryEquivalentIntegerDecimalRequests.UnsupportedCalls(partition))
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(call);
                await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
                await Assert.That(error.Message).IsEqualTo(Unsupported);
            }
            await Assert.That(QueryEquivalentApplicationNumber.Calls).IsEqualTo(count);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store, image, cut, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task MatrixAsync(QueryEngine engine, PartitionRef partition, ErrorCode? denied,
        long cut, CancellationToken token)
    {
        foreach (var kind in Enum.GetValues<QueryEquivalentIntegerKind>())
        {
            foreach (var boundary in new[] { false, true })
            {
                await CaseAsync(engine, partition, kind, boundary, denied, cut, token);
            }
        }
    }
    private static async Task CaseAsync(QueryEngine engine, PartitionRef partition, QueryEquivalentIntegerKind kind,
        bool boundary, ErrorCode? denied, long cut, CancellationToken token)
    {
        var minimum = boundary ? Maximum(kind) - Fraction : Threshold;
        var ast = QueryEquivalentIntegerDecimalRequests.Ast(partition, kind, minimum);
        var csharp = QueryEquivalentIntegerDecimalRequests.CSharp(partition, kind, minimum);
        var expectedFilter = new Comparison(new FieldOperand(PathPrefix + Field(kind)), Compare,
            ValueOperand.Create(minimum));
        await EqualAsync(expectedFilter, (Comparison)csharp.Query.Filter!);
        var expected = new QueryPage([.. Expected(boundary)], null, cut, Scan);
        Func<QueryPage>[] calls =
            [() => engine.Execute(Subject, QueryEquivalentIntegerDecimalRequests.Sql(partition, kind, minimum),
                cancellationToken: token),
             () => engine.ExecuteAst(Subject, ast, cancellationToken: token),
             () => engine.ExecuteAst(Subject, csharp, cancellationToken: token),
             () => engine.ExecuteAst(Subject, Q2CapabilityFixtures.NativeRoundTrip(csharp), cancellationToken: token)];
        foreach (var call in calls)
        {
            if (denied is { } error)
            {
                await RefusedAsync(call, error);
            }
            else
            {
                await EqualAsync(expected, call());
            }
        }
    }
    private static async Task RefusedAsync(Func<QueryPage> call, ErrorCode expected)
    {
        QueryPage? page = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => page = call());
        await Assert.That(error.Code).IsEqualTo(expected);
        await Assert.That(page).IsNull();
    }
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
