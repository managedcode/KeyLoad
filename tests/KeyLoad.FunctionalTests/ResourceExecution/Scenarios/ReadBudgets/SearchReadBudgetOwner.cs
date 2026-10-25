using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class SearchReadBudgetOwner
{
    internal static DatabaseEngine Create(TestDatabase fixture, ZoneTreeStore store, DatabaseLimits limits)
    {
        var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(limits),
            UnitExecutionOptions.RuntimeClaimsExecution(), null, fixture.Database.EvaluationClock);
        return create(store);
    }
}
