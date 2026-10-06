namespace KeyLoad.UnitTests.Features.TestInfrastructure;

/// <summary>Keeps a borrowed provider and its monotonic start together across test helper joins.</summary>
internal sealed class TestElapsedClock(TimeProvider provider)
{
    private readonly long started = provider.GetTimestamp();

    internal TimeProvider Provider => provider;
    internal TimeSpan Elapsed => provider.GetElapsedTime(started);
}
