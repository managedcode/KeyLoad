using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.FunctionalTests.QueryExecution.RelationalQueryBindingFixture;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class RelationalQueryBindingAssertions
{
    private const string FlagParameter = "flag";
    private const string ValueParameter = "value";

    internal static Task InvalidAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition, CancellationToken token)
        => RefusalsAsync(engine, store, partition, ErrorCode.Validation, Detail, token);

    internal static async Task DeniedAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition, CancellationToken token)
    {
        var request = Invalid(partition).First();
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var cut = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var before = store.GetReadDiagnostics();
        foreach (var call in Calls(engine, partition, request, token))
        {
            await RefusalAsync(call, ErrorCode.PermissionDenied, UseDenied);
        }
        await Assert.That(store.GetReadDiagnostics().RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        await CapabilityOutputOperations.ConserveAsync(store, image, cut, identity);
    }

    private static async Task RefusalsAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition,
        ErrorCode code, string detail, CancellationToken token)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var cut = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var before = store.GetReadDiagnostics();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            foreach (var request in Invalid(partition))
            {
                foreach (var call in Calls(engine, partition, request, token))
                {
                    await RefusalAsync(call, code, detail);
                }
            }
            await Assert.That(store.GetReadDiagnostics().RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store, image, cut, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Func<QueryPage>[] Calls(QueryEngine engine, PartitionRef partition,
        (string Suffix, AstQueryRequest Ast) request, CancellationToken token)
        => [() => engine.Execute(Reader, new(partition, Projection + request.Suffix,
                request.Ast.Parameters, AllowFullScan: true), cancellationToken: token),
            () => engine.ExecuteAst(Reader, request.Ast, cancellationToken: token),
            () => engine.ExecuteAst(Reader, Q2CapabilityFixtures.JsonRoundTrip(request.Ast), cancellationToken: token),
            () => engine.ExecuteAst(Reader, Q2CapabilityFixtures.RuntimeRoundTrip(request.Ast), cancellationToken: token)];

    private static async Task RefusalAsync(Func<QueryPage> execute, ErrorCode code, string detail)
    {
        QueryPage? page = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => page = execute());
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(error.Message).IsEqualTo(detail);
        await Assert.That(page).IsNull();
    }

    internal static async Task HealthyAsync(QueryEngine engine, ZoneTreeStore store, PartitionRef partition, CancellationToken token)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var cut = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var field = new FieldOperand(Total);
        var healthy = new (string Suffix, AstQueryRequest Ast, bool Found)[]
        {
            (" WHERE total = ratio AND label = at", Ast(partition, new Logical(
                new Comparison(field, "=", new FieldOperand("/ratio")), "AND",
                new Comparison(new FieldOperand("/label"), "=", new FieldOperand("/at")))), true),
            (" WHERE total IN (NULL, 10)", Ast(partition, new InPredicate(field, [ValueOperand.Create(null), ValueOperand.Create(10)], false)), true),
            (" WHERE total = NULL", Ast(partition, new Comparison(field, "=", ValueOperand.Create(null))), false),
            (" WHERE optional IS NULL", Ast(partition, new NullTest(new FieldOperand("/optional"), false, false)), true),
            (" WHERE active = @flag", Ast(partition, new Comparison(new FieldOperand("/active"), "=", new ParameterOperand(FlagParameter)),
                parameters: new() { [FlagParameter] = System.Text.Json.JsonSerializer.SerializeToElement(true) }), true),
            (" WHERE total = 10.5", Ast(partition, new Comparison(field, "=", ValueOperand.Create(10.5m))), false),
            (" WHERE total = @value", Ast(partition, new Comparison(field, "=", new ParameterOperand(ValueParameter)),
                parameters: new() { [ValueParameter] = System.Text.Json.JsonSerializer.SerializeToElement(10) }), true)
        };
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            foreach (var request in healthy)
            {
                var expected = new QueryPage(request.Found ? [new(Id, Revision, "{\"amount\":10}", RedactedFields: [])] : [],
                    null, cut, "bounded-full-scan");
                foreach (var call in Calls(engine, partition, (request.Suffix, request.Ast), token))
                {
                    await BinderFieldLineagePages.EqualAsync(expected, call());
                }
            }
            await UnknownProjectionAsync(engine, partition, cut, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store, image, cut, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static Task RecordAsync(TestDatabase fixture, DatabaseEngine owner, PartitionRef partition, CancellationToken token)
        => BinderFieldLineagePages.EqualAsync(new DocumentResult(fixture.ExpectedReference(new(partition, Collection, Id)), Revision, Body, false, []),
            owner.GetDocument(Root, new(partition, Collection, Id), cancellationToken: token));

    private static async Task UnknownProjectionAsync(QueryEngine engine, PartitionRef partition, long cut, CancellationToken token)
    {
        var original = Ast(partition, new Comparison(new FieldOperand(Total), "=", ValueOperand.Create(10)));
        var request = original with
        {
            Query = original.Query with
            {
                Projection = [new("/absent", "amount")]
            }
        };
        var expected = new QueryPage([new(Id, Revision, "{\"amount\":null}", RedactedFields: [])], null, cut, "bounded-full-scan");
        var sql = engine.Execute(Reader, new(partition, "SELECT absent AS amount FROM typed_binding WHERE total = 10",
            AllowFullScan: true), cancellationToken: token);
        await BinderFieldLineagePages.EqualAsync(expected, sql);
        await BinderFieldLineagePages.EqualAsync(expected, engine.ExecuteAst(Reader, request, cancellationToken: token));
        await BinderFieldLineagePages.EqualAsync(expected, engine.ExecuteAst(Reader, Q2CapabilityFixtures.JsonRoundTrip(request), cancellationToken: token));
        await BinderFieldLineagePages.EqualAsync(expected, engine.ExecuteAst(Reader, Q2CapabilityFixtures.RuntimeRoundTrip(request), cancellationToken: token));
    }
}
