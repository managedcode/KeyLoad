namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[ConfigurationOptions]
internal sealed record ConnectionRf3Options
{
    private const int PollMilliseconds = 100;
    private const int ConnectionSeconds = 30;
    private const int ClosedObservationSeconds = 20;
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(PollMilliseconds);
    internal TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(ConnectionSeconds);
    internal TimeSpan ClosedObservationTimeout { get; init; } = TimeSpan.FromSeconds(ClosedObservationSeconds);
    internal TimeSpan IdleObservationTimeout
        => new KeyLoad.Orleans.GrainRoutingOptions().ConnectionIdleTimeout + ClosedObservationTimeout;
}
