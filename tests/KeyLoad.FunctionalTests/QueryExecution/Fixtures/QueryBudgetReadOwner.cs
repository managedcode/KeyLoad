using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryBudgetReadOwner
{
    internal static QueryEngine Create(TestDatabase fixture, DatabaseLimits limits)
    {
        var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(limits),
            UnitExecutionOptions.RuntimeClaimsExecution(), null, fixture.Database.EvaluationClock);
        return new(create(fixture.Store), UnitExecutionOptions.QueryExecution());
    }
}
