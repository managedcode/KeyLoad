namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryWorkloadCoverageAssertions
{
    internal static async Task AssertLiveMutationCoverage(int[] identityVisits, int[] transitions)
    {
        await Assert.That(identityVisits.All(visits => visits == 1)).IsTrue();
        foreach (var round in LiveQueryTestSchedule.TransitionRounds)
        {
            var expectedCount = round == LiveQueryTestSchedule.FinalUpdateRound
                ? LiveQueryTestSchedule.FinalUpdateCount
                : LiveQueryTestSchedule.IdentityCount;
            await Assert.That(transitions[round]).IsEqualTo(expectedCount);
        }
    }

    internal static async Task AssertDocumentCoverage(int[] documentCoverage, int statusCount, int numericValueCount)
    {
        for (var statusIndex = 0; statusIndex < statusCount; statusIndex++)
        {
            for (var value = 0; value < numericValueCount; value++)
            {
                await Assert.That(documentCoverage[statusIndex * numericValueCount + value] > 1).IsTrue();
            }
        }
    }
}
