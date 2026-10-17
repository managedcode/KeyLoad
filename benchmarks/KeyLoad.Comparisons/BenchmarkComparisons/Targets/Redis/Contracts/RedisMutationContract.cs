namespace KeyLoad.Comparisons.Targets;

internal static class RedisMutationContract
{
    internal static void RequireSet(bool changed, Scenario scenario)
    {
        if (scenario is not (Scenario.DocumentWrite or Scenario.DocumentUpdate))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        if (!changed)
        {
            throw new ComparisonFailureException(scenario == Scenario.DocumentWrite
                ? ComparisonMutationFailures.CreateConflict : ComparisonMutationFailures.UpdateMissing);
        }
    }

    internal static void RequireDelete(bool deleted)
    {
        if (!deleted)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.DeleteMissing);
        }
    }
}
