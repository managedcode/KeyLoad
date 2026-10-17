namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Admits only the shared native multi-test session entry point.</summary>
internal static class IsolatedBenchmarkResources
{
    internal static void Add(IDistributedApplicationBuilder builder)
    {
        var session = BenchmarkSessionResourceOptions.Read(builder.Configuration).Value;
        if (!session.Session)
        {
            throw new InvalidOperationException(BenchmarkSessionResourceOptions.Profile);
        }
        BenchmarkSessionResources.Add(builder, session);
    }
}
