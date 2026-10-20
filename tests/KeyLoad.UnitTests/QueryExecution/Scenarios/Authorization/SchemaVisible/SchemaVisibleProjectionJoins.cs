using KeyLoad.Query;
using J = KeyLoad.UnitTests.Features.QueryExecution.SqlEventReferenceJoinFixture;
using P = KeyLoad.UnitTests.Features.QueryExecution.SchemaVisibleProjectionProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SchemaVisibleProjectionJoins
{
    private const string ReferencePath = "/to";
    private const string Classification = "schema-reference";
    private const string Name = "name";
    private const string SupportedPath = "/payload/public";
    private const string UnknownPath = "/absent";
    private const string Equal = "=";
    private const string SupportedPredicate = "e.payload.score >= @minimum";
    private const string SupportedReplacement = "e.payload.[public] = @minimum";
    private const string UnknownReplacement = "e.absent = @minimum";

    internal static void Configure(TestDatabase fixture)
    {
        fixture.Configure(J.StreamSet, ResourceKind.StreamSet, fields:
            [new(ReferencePath, Classification, RawReadGrant: J.RefRead, RawUseGrant: J.RefUse, SchemaReadGrant: P.SchemaGrant)]);
        fixture.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(fixture.Partition.TenantId,
            fixture.Partition.DatabaseId, new(J.Target, ResourceKind.Collection, fixture.Partition.TransactionDomainId)
            {
                RelationalSchema = new(J.PrimaryKeyColumn,
                    [new(J.PrimaryKeyColumn, RelationalColumnType.Text), new(Name, RelationalColumnType.Text)])
            })).Get<ResourceDefinition>();
    }

    internal static IEnumerable<(QueryRequest Sql, AstQueryRequest Ast)> Requests(PartitionRef partition)
    {
        foreach (var supported in new[] { true, false })
        {
            var request = J.Request(partition);
            var ast = J.Ast(partition);
            yield return (request with
            {
                Sql = request.Sql.Replace(SupportedPredicate, supported ? SupportedReplacement : UnknownReplacement,
                    StringComparison.Ordinal)
            }, ast with
            {
                Query = ast.Query with
                {
                    Filter = new Comparison(new FieldOperand(supported ? SupportedPath : UnknownPath), Equal,
                        new ParameterOperand(J.MinimumParameter))
                }
            });
        }
    }
}
