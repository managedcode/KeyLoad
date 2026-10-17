namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class FreshArchiveConservationProtocol
{
    internal const string Prefix = "keyload-fresh-archive-conservation-";
    internal const string Source = "source";
    internal const string Archive = "archive";
    internal const string Target = "target";
    internal const string Collection = "conservation-documents";
    internal const string Document = "original";
    internal const string Json = "{\"value\":19}";
    internal const string PutKind = "putDocument";
    internal const string Root = "root";
    internal const string FloorFamily = "resource-retirement-conservation-floor-v1";
    internal const string StepFamily = "resource-retirement-conservation-step-v1";
    internal const string Invalid = "The original fresh archive conservation invariant failed.";
}
