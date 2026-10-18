using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class SearchReadBudgetOwner
{
    internal static DatabaseEngine Create(TestDatabase fixture, ZoneTreeStore store, long maximumBytes)
        => new(store, fixture.Database.Authorization,
            UnitExecutionOptions.DatabaseLimits(new()
            {
                MaxQueryReadBytes = maximumBytes,
                MaxConcurrentQueries = fixture.Database.Limits.MaxConcurrentQueries
            }),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution());
}
