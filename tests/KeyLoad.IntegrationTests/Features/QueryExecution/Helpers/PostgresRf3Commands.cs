using Npgsql;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Commands
{
    private const string JoinedSql = "SELECT l.order_id AS order_id, l.detail AS left_detail, r.name AS right_name, r.id AS customer_id FROM budgetorders AS l INNER JOIN budgetcustomers AS r ON l.customer_id = r.id ORDER BY l.order_id ASC LIMIT 10";
    private const string UnsupportedSql = "BEGIN";
    private const string InvalidCommand = "The native SQL regression command is outside its frozen literal inventory.";

    internal static NpgsqlCommand Create(string sql, NpgsqlConnection connection)
        => sql switch
        {
            PostgresRf3State.ScalarSql => new(PostgresRf3State.ScalarSql, connection),
            JoinedSql => new(JoinedSql, connection),
            PostgresRf3Protocol.CommitSql => new(PostgresRf3Protocol.CommitSql, connection),
            PostgresRf3BackpressureProtocol.Sql => new(PostgresRf3BackpressureProtocol.Sql, connection),
            UnsupportedSql => new(UnsupportedSql, connection),
            SqlNullOrderingRf3Protocol.AscFirst => new(SqlNullOrderingRf3Protocol.AscFirst, connection),
            SqlNullOrderingRf3Protocol.AscLast => new(SqlNullOrderingRf3Protocol.AscLast, connection),
            SqlNullOrderingRf3Protocol.DescFirst => new(SqlNullOrderingRf3Protocol.DescFirst, connection),
            SqlNullOrderingRf3Protocol.DescLast => new(SqlNullOrderingRf3Protocol.DescLast, connection),
            SqlNullOrderingRf3Protocol.BadSql => new(SqlNullOrderingRf3Protocol.BadSql, connection),
            SqlFetchCountRf3Protocol.UnorderedOne => new(SqlFetchCountRf3Protocol.UnorderedOne, connection),
            SqlFetchCountRf3Protocol.FirstOne => new(SqlFetchCountRf3Protocol.FirstOne, connection),
            SqlFetchCountRf3Protocol.NextOne => new(SqlFetchCountRf3Protocol.NextOne, connection),
            SqlFetchCountRf3Protocol.FirstTwo => new(SqlFetchCountRf3Protocol.FirstTwo, connection),
            SqlFetchCountRf3Protocol.NextThree => new(SqlFetchCountRf3Protocol.NextThree, connection),
            SqlFetchCountRf3Protocol.SingularTwo => new(SqlFetchCountRf3Protocol.SingularTwo, connection),
            SqlFetchCountRf3Protocol.BadSql => new(SqlFetchCountRf3Protocol.BadSql, connection),
            SqlFetchCountRf3Protocol.TiesSql => new(SqlFetchCountRf3Protocol.TiesSql, connection),
            _ => throw new InvalidOperationException(InvalidCommand)
        };

    internal static NpgsqlCommand Plan(string sql, int index, NpgsqlConnection connection)
        => sql == PostgresRf3State.ScalarSql ? ScalarPlan((PlanSlot)index, connection)
            : sql == JoinedSql ? JoinedPlan((PlanSlot)index, connection)
            : throw new InvalidOperationException(InvalidCommand);

    internal static NpgsqlCommand RefusedPlan(string sql, NpgsqlConnection connection)
        => sql switch
        {
            PostgresRf3State.ScalarSql => new(PostgresRf3State.ScalarSql + Refused, connection),
            JoinedSql => new(JoinedSql + Refused, connection),
            _ => throw new InvalidOperationException(InvalidCommand)
        };

    private static NpgsqlCommand ScalarPlan(PlanSlot index, NpgsqlConnection connection)
        => index switch
        {
            PlanSlot.First => new(PostgresRf3State.ScalarSql + First, connection),
            PlanSlot.Second => new(PostgresRf3State.ScalarSql + Second, connection),
            PlanSlot.Third => new(PostgresRf3State.ScalarSql + Third, connection),
            PlanSlot.Fourth => new(PostgresRf3State.ScalarSql + Fourth, connection),
            PlanSlot.Fifth => new(PostgresRf3State.ScalarSql + Fifth, connection),
            PlanSlot.Sixth => new(PostgresRf3State.ScalarSql + Sixth, connection),
            PlanSlot.Seventh => new(PostgresRf3State.ScalarSql + Seventh, connection),
            PlanSlot.Eighth => new(PostgresRf3State.ScalarSql + Eighth, connection),
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
    private const string First = " /* native-plan-0 */";
    private const string Second = " /* native-plan-1 */";
    private const string Third = " /* native-plan-2 */";
    private const string Fourth = " /* native-plan-3 */";
    private const string Fifth = " /* native-plan-4 */";
    private const string Sixth = " /* native-plan-5 */";
    private const string Seventh = " /* native-plan-6 */";
    private const string Eighth = " /* native-plan-7 */";
    private const string Refused = " /* native-plan-refused */";
}
