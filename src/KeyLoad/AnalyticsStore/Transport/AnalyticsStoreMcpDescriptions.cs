namespace KeyLoad.Server;

internal static class AnalyticsStoreMcpDescriptions
{
    private const string Read = "Read one bounded authorized canonical event page from a named analytics store and atomic partition. "
        + "Use a half-open UTC time range and ordinal event-ID continuation; each call checks current persisted row and field rights.";
    private const string Aggregate = "Read complete bounded execution aggregates from one authorized canonical cut of a named analytics store. "
        + "Select declared dimensions and metrics; incomplete history, execution plans or group overflow refuse the operation.";

    internal static string? Find(string name) => name switch
    {
        AnalyticsStoreProtocol.ReadTool => Read,
        AnalyticsStoreProtocol.AggregateTool => Aggregate,
        _ => null
    };
}
