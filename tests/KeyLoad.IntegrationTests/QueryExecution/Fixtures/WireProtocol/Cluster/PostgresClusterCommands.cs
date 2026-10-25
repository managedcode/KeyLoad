using Npgsql;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class PostgresClusterCommands
{
    private const string JoinedSql = "SELECT l.order_id AS order_id, l.detail AS left_detail, r.name AS right_name, r.id AS customer_id FROM budgetorders AS l INNER JOIN budgetcustomers AS r ON l.customer_id = r.id ORDER BY l.order_id ASC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY";
    private const string UnsupportedSql = "BEGIN";
    private const string InvalidCommand = "The execution SQL checks command is outside its frozen literal inventory.";

    internal static NpgsqlCommand Create(string sql, NpgsqlConnection connection)
        => sql switch
        {
            PostgresClusterState.ScalarSql => new(PostgresClusterState.ScalarSql, connection),
            JoinedSql => new(JoinedSql, connection),
            PostgresClusterProtocol.CommitSql => new(PostgresClusterProtocol.CommitSql, connection),
            PostgresClusterBackpressureProtocol.Sql => new(PostgresClusterBackpressureProtocol.Sql, connection),
            UnsupportedSql => new(UnsupportedSql, connection),
            SqlNullOrderingClusterProtocol.AscFirst => new(SqlNullOrderingClusterProtocol.AscFirst, connection),
            SqlNullOrderingClusterProtocol.AscLast => new(SqlNullOrderingClusterProtocol.AscLast, connection),
            SqlNullOrderingClusterProtocol.DescFirst => new(SqlNullOrderingClusterProtocol.DescFirst, connection),
            SqlNullOrderingClusterProtocol.DescLast => new(SqlNullOrderingClusterProtocol.DescLast, connection),
            SqlNullOrderingClusterProtocol.BadSql => new(SqlNullOrderingClusterProtocol.BadSql, connection),
            SqlFetchCountClusterProtocol.UnorderedOne => new(SqlFetchCountClusterProtocol.UnorderedOne, connection),
            SqlFetchCountClusterProtocol.FirstOne => new(SqlFetchCountClusterProtocol.FirstOne, connection),
            SqlFetchCountClusterProtocol.NextOne => new(SqlFetchCountClusterProtocol.NextOne, connection),
            SqlFetchCountClusterProtocol.FirstTwo => new(SqlFetchCountClusterProtocol.FirstTwo, connection),
            SqlFetchCountClusterProtocol.NextThree => new(SqlFetchCountClusterProtocol.NextThree, connection),
            SqlFetchCountClusterProtocol.SingularTwo => new(SqlFetchCountClusterProtocol.SingularTwo, connection),
            SqlFetchCountClusterProtocol.BadSql => new(SqlFetchCountClusterProtocol.BadSql, connection),
            SqlFetchCountClusterProtocol.TiesSql => new(SqlFetchCountClusterProtocol.TiesSql, connection),
            PostgresInt16ClusterProtocol.DriverSql => new(PostgresInt16ClusterProtocol.DriverSql, connection),
            _ => throw new InvalidOperationException(InvalidCommand)
        };

    internal static NpgsqlCommand Plan(string sql, int index, NpgsqlConnection connection)
        => sql == PostgresClusterState.ScalarSql ? ScalarPlan((PlanSlot)index, connection)
            : sql == JoinedSql ? JoinedPlan((PlanSlot)index, connection)
            : throw new InvalidOperationException(InvalidCommand);

    internal static NpgsqlCommand RefusedPlan(string sql, NpgsqlConnection connection)
        => sql switch
        {
            PostgresClusterState.ScalarSql => new(PostgresClusterState.ScalarSql + Refused, connection),
            JoinedSql => new(JoinedSql + Refused, connection),
            _ => throw new InvalidOperationException(InvalidCommand)
        };

    private static NpgsqlCommand ScalarPlan(PlanSlot index, NpgsqlConnection connection)
        => index switch
        {
            PlanSlot.First => new(PostgresClusterState.ScalarSql + First, connection),
            PlanSlot.Second => new(PostgresClusterState.ScalarSql + Second, connection),
            PlanSlot.Third => new(PostgresClusterState.ScalarSql + Third, connection),
            PlanSlot.Fourth => new(PostgresClusterState.ScalarSql + Fourth, connection),
            PlanSlot.Fifth => new(PostgresClusterState.ScalarSql + Fifth, connection),
            PlanSlot.Sixth => new(PostgresClusterState.ScalarSql + Sixth, connection),
            PlanSlot.Seventh => new(PostgresClusterState.ScalarSql + Seventh, connection),
            PlanSlot.Eighth => new(PostgresClusterState.ScalarSql + Eighth, connection),
            _ => throw new InvalidOperationException(InvalidCommand)
        };

    private static NpgsqlCommand JoinedPlan(PlanSlot index, NpgsqlConnection connection)
        => index switch
        {
            PlanSlot.First => new(JoinedSql + First, connection),
            PlanSlot.Second => new(JoinedSql + Second, connection),
            PlanSlot.Third => new(JoinedSql + Third, connection),
            PlanSlot.Fourth => new(JoinedSql + Fourth, connection),
            PlanSlot.Fifth => new(JoinedSql + Fifth, connection),
            PlanSlot.Sixth => new(JoinedSql + Sixth, connection),
            PlanSlot.Seventh => new(JoinedSql + Seventh, connection),
            PlanSlot.Eighth => new(JoinedSql + Eighth, connection),
            _ => throw new InvalidOperationException(InvalidCommand)
        };

    private enum PlanSlot
    {
        First, Second, Third, Fourth, Fifth, Sixth, Seventh, Eighth
    }
    private const string First = " /* execution-plan-0 */";
    private const string Second = " /* execution-plan-1 */";
    private const string Third = " /* execution-plan-2 */";
    private const string Fourth = " /* execution-plan-3 */";
    private const string Fifth = " /* execution-plan-4 */";
    private const string Sixth = " /* execution-plan-5 */";
    private const string Seventh = " /* execution-plan-6 */";
    private const string Eighth = " /* execution-plan-7 */";
    private const string Refused = " /* execution-plan-refused */";
}
