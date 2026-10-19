using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ModelRootPolicyRequests
{
    internal static string Source(ModelRootPolicyFixture fixture) => fixture.Queue
        ? "QUEUE_MESSAGES('root-source')" : "EVENTS('root-source','stream')";
    internal static AstQueryRequest Ast(ModelRootPolicyFixture fixture, bool filter = false, bool order = false, bool explicitValue = false)
        => new(fixture.Harness.Partition, new(ModelRootPolicyFixture.Source, null,
            explicitValue ? [new("/" + fixture.RootComponent + "/secret", "secret")] : [new("*", "*")],
            filter ? Predicate(fixture) : null,
            order ? [new("/" + fixture.RootComponent + "/secret", false)] : [new(fixture.ModelOrder, false)], 10,
            ModelSource: fixture.Model), AllowFullScan: true);
    internal static QueryRequest Sql(ModelRootPolicyFixture fixture, bool filter = false, bool order = false, bool explicitValue = false)
        => new(fixture.Harness.Partition, "SELECT TOP (10) " + (explicitValue ? fixture.RootComponent + ".secret AS secret" : "*")
            + " FROM " + Source(fixture) + (filter ? " WHERE " + fixture.RootComponent + ".secret = '" + fixture.RootValue + "'" : "")
            + " ORDER BY " + (order ? fixture.RootComponent + ".secret" : fixture.Queue ? "id" : "revision"), AllowFullScan: true);
    internal static AstQueryRequest LiteralPublic(ModelRootPolicyFixture fixture)
        => Ast(fixture) with
        {
            Query = Ast(fixture).Query with
            {
                Projection = [new(fixture.HeaderRoot ? "/payload/public" : "/headers/public", "value")]
            }
        };
    internal static AstQueryRequest Join(ModelRootPolicyFixture fixture)
        => new(fixture.Harness.Partition, new(ModelRootPolicyFixture.Source, "e",
            [new("/" + fixture.RootComponent + "/secret", "secret", "e"), new("/name", "name", "d")], Predicate(fixture),
            [new(fixture.ModelOrder, false)], 10, ModelSource: fixture.Model,
            InnerJoin: new(ModelRootPolicyFixture.Target, "d", "/payload/to", "/entity_ref")), AllowFullScan: true, AstVersion: SqlOperationProtocol.DefaultQueryDialectVersion);
    private static Comparison Predicate(ModelRootPolicyFixture fixture)
        => new(new FieldOperand("/" + fixture.RootComponent + "/secret"), "=", ValueOperand.Create(fixture.RootValue));
}
