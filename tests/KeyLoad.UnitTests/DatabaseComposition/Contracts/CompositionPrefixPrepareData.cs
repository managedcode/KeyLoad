namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionPrefixPrepareData
{
    internal const string Root = "root";
    internal const string Reader = "prefix-denied";
    internal const string RevokedReader = "prefix-revoked-endpoint";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string From = "prefix-from";
    internal const string To = "prefix-to";
    internal const string Graph = "prefix-graph";
    internal const string RoundGraph = "prefix-round";
    internal const string Queue = "prefix-queue";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Message = "message";
    internal const string Edge = "edge";
    internal const string Forward = "forward-";
    internal const string Reverse = "reverse-";
    internal const string Round = "round-";
    internal const string Rollback = "rollback-";
    internal const string Label = "related";
    internal const string Json = "{\"id\":\"first\"}";
    internal const string OtherJson = "{\"id\":\"second\"}";
    internal const string EmptyJson = "{}";
    internal const string Put = "putDocument";
    internal const string Enqueue = "enqueue";
    internal const string Upsert = "upsertEdge";
    internal const string Cancel = "cancelQueueMessage";
    internal const string InvalidLink = "The queue graph link is invalid.";
    internal const string ClockBehind = "The operation time is behind the committed storage time.";
    internal const string Retiring = "The resource lifetime is retiring.";
    internal const string Marker = "clock-rollback-marker";
    internal const string MarkerJson = "{\"id\":\"clock-rollback-marker\"}";
    internal const string SourceOverflow = "The database composition source exceeds its budget.";
    internal const string DirectoryPrefix = "keyload-prefix-prepare-";
    internal const string GuidFormat = "N";
    internal const int Absent = 0;
    internal const int FirstRevision = 1;
    internal const int CancelRevision = 2;
    internal const int EffectCap = 3;
    internal const int BindingCount = 4;
    internal const int ColdCount = 2;
    internal const int ScanBound = 100;
    internal const int TemporalSeconds = 1;
}
