using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class RelationalQueryBindingFixture
{
    private const string KeyColumn = "key";
    private const string ValueParameter = "value";
    internal const string Collection = "typed_binding";
    internal const string Reader = "typed-binding-reader";
    internal const string Root = "root";
    internal const string Id = "one";
    internal const string Body = """{"key":"one","total":10,"ratio":10.0,"active":true,"label":"2026-10-15T00:00:00Z","at":"2026-10-15T00:00:00Z","optional":null}""";
    internal const string Detail = "The typed query binding is invalid.";
    internal const string UseDenied = "The query requires a protected field-use grant.";
    internal const string Projection = "SELECT total AS amount FROM typed_binding";
    internal const string Total = "/total";
    internal const long InitialEpoch = 1L;
    internal const long DeniedEpoch = 2L;
    internal const long RepairedEpoch = 3L;
    internal const int Limit = 100;
    internal const long Revision = 1L;

    internal static void Provision(TestDatabase fixture)
    {
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, fixture.Partition.TransactionDomainId)
        {
            RelationalSchema = new(KeyColumn, [new(KeyColumn, RelationalColumnType.Text),
                new("total", RelationalColumnType.WholeNumber), new("ratio", RelationalColumnType.FixedPoint),
                new("active", RelationalColumnType.Boolean), new("label", RelationalColumnType.Text),
                new("at", RelationalColumnType.UtcTimestamp), new("optional", RelationalColumnType.Text, true)]),
            FieldPolicies = [new(Total, "pii")]
        };
        fixture.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(fixture.Partition.TenantId,
            fixture.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
        Policy(fixture, InitialEpoch, true);
    }

    internal static void Policy(TestDatabase fixture, long epoch, bool use)
        => fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader,
            fixture.Partition.TenantId, [new(fixture.Partition.DatabaseId, Collection,
                Capability.Query | Capability.DocumentsRead)], use ? ["pii.read", "pii.use"] : ["pii.read"])
        {
            PolicyEpoch = epoch
        })).Get<PrincipalRecord>();

    internal static (CommandRequest Command, CommitReceipt Receipt) Seed(TestDatabase fixture)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition, [new PutDocument(Collection, Id, Body)]);
        return (command, fixture.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>());
    }

    internal static AstQueryRequest Ast(PartitionRef partition, Predicate? predicate,
        ImmutableArray<Ordering> order = default, Dictionary<string, JsonElement>? parameters = null)
        => new(partition, new(Collection, null, [new(Total, "amount")], predicate,
            order.IsDefault ? [] : order, Limit), parameters, AllowFullScan: true);

    internal static IEnumerable<(string Suffix, AstQueryRequest Ast)> Invalid(PartitionRef partition)
    {
        var field = new FieldOperand(Total);
        var text = ValueOperand.Create("text");
        yield return (" WHERE total = 'text'", Ast(partition, new Comparison(field, "=", text)));
        yield return (" WHERE total IN (10, 'text')", Ast(partition, new InPredicate(field, [ValueOperand.Create(10), text], false)));
        yield return (" WHERE label = total", Ast(partition, new Comparison(new FieldOperand("/label"), "=", field)));
        yield return (" WHERE absent = 10", Ast(partition, new Comparison(new FieldOperand("/absent"), "=", ValueOperand.Create(10))));
        yield return (" ORDER BY absent", Ast(partition, null, [new("/absent", false)]));
        yield return (" WHERE total.part = 10", Ast(partition, new Comparison(new FieldOperand("/total/part"), "=", ValueOperand.Create(10))));
        var nested = new Negation(new Logical(new Comparison(field, "=", ValueOperand.Create(10)), "AND",
            new InPredicate(field, [text], false)));
        yield return (" WHERE NOT (total = 10 AND total IN ('text'))", Ast(partition, nested));
        yield return (" WHERE total = @value", Ast(partition, new Comparison(field, "=", new ParameterOperand(ValueParameter)),
            parameters: new()
            {
                [ValueParameter] = JsonSerializer.SerializeToElement("text")
            }));
    }
}
