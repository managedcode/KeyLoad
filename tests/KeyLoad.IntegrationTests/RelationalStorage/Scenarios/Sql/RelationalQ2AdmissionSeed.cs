using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record RelationalQ2AdmissionSeed(RelationalSqlRf3Scenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Command, CommitReceipt Receipt)
{
    internal SqlOperationRequest Sql => new(Scenario.Partition,
        RelationalSqlRf3JoinAdmissionSeed.SqlText, AllowFullScan: true,
        QueryDialectVersion: RelationalSqlRf3JoinAdmissionSeed.Dialect);
    internal QueryRequest Query => new(Scenario.Partition,
        RelationalSqlRf3JoinAdmissionSeed.SqlText, AllowFullScan: true,
        QueryDialectVersion: RelationalSqlRf3JoinAdmissionSeed.Dialect);
    internal global::KeyLoad.Query.AstQueryRequest Ast => new(Scenario.Partition,
        new global::KeyLoad.Query.SelectQuery(RelationalSqlRf3Tokens.Table, "l",
            [new("/key", "order_key", "l"), new("/name", "customer_name", "r"),
                new("/title", "customer_id", "l")], null, [new("/key", false)],
            RelationalSqlRf3JoinAdmissionSeed.Limit,
            InnerJoin: new(RelationalSqlRf3JoinAdmissionSeed.Right, "r", "/title", "/id")),
        AllowFullScan: true, AstVersion: RelationalSqlRf3JoinAdmissionSeed.Dialect);
}
