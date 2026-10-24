namespace KeyLoad.UnitTests.TestInfrastructure;

/// <summary>Coordinates execution logging captures which configure one process-wide EventSource filter.</summary>
internal static class LoggingEventSourceIsolation
{
    /// <summary>Names the shared TUnit scheduling boundary for the actual provider's listener lifetime.</summary>
    internal const string Key = "keyload-logging-eventsource";
}
