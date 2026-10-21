using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionClusterProtocol;
using S = KeyLoad.IntegrationTests.Features.Authorization.SafeProjectorOperationClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionClusterJoins
{
    internal const string EventsSuffix = "-schema-events";
    internal const string TargetSuffix = "-schema-target";
    private const string SourceAlias = "e";
    private const string TargetAlias = "r";
    private const string ReferencePath = "/to";
    private const string ReferenceOperand = "/payload/to";
    private const string EntityOperand = "/entity_ref";
    private const string PrimaryKey = "id";
    private const string Name = "name";
    private const string NamePath = "/name";
    private const string RevisionPath = "/revision";
    private const string Stream = "source";
    private const long Generation = 1;
    private const string Supported = "/payload/public";
    private const string Absent = "/absent";
    private const string Probe = "probe";
    private const string Prefix = "SELECT TOP (10) r.name AS exposed FROM EVENTS('";
    private const string Middle = "','source',1) AS e INNER JOIN [";
    private const string BeforePredicate = "] AS r ON e.payload.[to] = r.entity_ref WHERE ";
    private const string SupportedSql = "e.payload.[public]";
    private const string AbsentSql = "e.absent";
    private const string Suffix = " = 'probe' ORDER BY e.revision ASC";

    internal static async Task ConfigureAsync(RequestCqrsClusterCallers root, SafeProjectorOperationClusterHistory history,
        CancellationToken token)
    {
        var state = history.State;
        ResourceDefinition[] resources =
        [new(state.Collection + EventsSuffix, ResourceKind.StreamSet, state.Partition.TransactionDomainId)
        {
            FieldPolicies = [new(ReferencePath, S.Classification, RawReadGrant: S.ReadGrant,
                RawUseGrant: S.UseGrant, SchemaReadGrant: P.SchemaGrant)]
        }, new(state.Collection + TargetSuffix, ResourceKind.Collection, state.Partition.TransactionDomainId)
        {
            RelationalSchema = new(PrimaryKey, [new(PrimaryKey, RelationalColumnType.Text), new(Name, RelationalColumnType.Text)])
        }];
        foreach (var resource in resources)
        {
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                    await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                        new(state.Partition.TenantId, state.Partition.DatabaseId, resource), token)));
            }, token);
        }
    }

    internal static IEnumerable<(QueryRequest Sql, AstQueryRequest Ast)> Requests(SafeProjectorOperationClusterState state)
    {
        foreach (var supported in new[] { true, false })
        {
            var events = state.Collection + EventsSuffix;
            var target = state.Collection + TargetSuffix;
            yield return (new(state.Partition, Prefix + events + Middle + target + BeforePredicate
                + (supported ? SupportedSql : AbsentSql) + Suffix, AllowFullScan: true, QueryDialectVersion: P.QueryDialect),
                new(state.Partition, new(events, SourceAlias, [new(NamePath, P.ValueAlias, TargetAlias)],
                    new Comparison(new FieldOperand(supported ? Supported : Absent), P.Equal, ValueOperand.Create(Probe)),
                    [new(RevisionPath, false)], P.Limit, ModelSource: new(ModelQuerySourceKind.Events, Stream, Generation),
                    InnerJoin: new(target, TargetAlias, ReferenceOperand, EntityOperand)), AllowFullScan: true, AstVersion: P.QueryDialect));
        }
    }
}
