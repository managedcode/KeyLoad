namespace KeyLoad.Core.Features.AnalyticsStore;

internal static class AnalyticsStoreNativeGroupLimit
{
    // Only classifies an actual native query failure when the unchanged authorized input proves this exact limit.
    internal static bool Exceeded(AnalyticsAggregateSnapshot snapshot)
    {
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot.Rows)
        {
            snapshot.Work.Check();
            var key = System.Text.Json.JsonSerializer.Serialize(snapshot.Request.GroupBy.Select(name => row.Dimensions[name]).ToArray(), JsonDefaults.Options);
            if (groups.Add(key) && groups.Count > snapshot.Request.MaxGroups)
            {
                return true;
            }
        }
        return false;
    }
}
