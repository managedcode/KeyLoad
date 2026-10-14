namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveReadCancellationR49Protocol
{
    internal const string Collection = "live-cancellation-docs";
    internal const string Document = "live-cancellation";
    internal const string InitialJson = "{\"value\":17}";
    internal const string CurrentJson = "{\"value\":29}";
    internal const string ReadUnavailable = "The read response is unavailable.";
    internal const string FullScan = "bounded-full-scan";
    internal const string PutDocument = "putDocument";
    internal const int InitialValue = 17;
    internal const int CurrentValue = 29;
    internal const long InitialRevision = 1;
    internal const long CurrentRevision = 2;
    internal const long InitialTail = 2;
    internal const long CurrentTail = 3;
    internal sealed record DocumentModel(int Value);
}
