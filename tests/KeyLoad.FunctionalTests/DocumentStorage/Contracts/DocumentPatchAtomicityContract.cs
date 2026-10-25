namespace KeyLoad.FunctionalTests.DocumentStorage;

internal static class DocumentPatchAtomicityContract
{
    internal const string Root = "root";
    internal const string Collection = "patch-atomic";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Index = "by-value";
    internal const string ValuePath = "/value";
    internal const string FirstBefore = "{\"value\":1}";
    internal const string FirstAfter = "{\"value\":2}";
    internal const string MissingBefore = "{\"details\":{},\"value\":10}";
    internal const string ArrayBefore = "{\"items\":[0],\"value\":10}";
    internal const string MissingRepair = "{\"details\":{\"missing\":{}},\"value\":10}";
    internal const string ArrayRepair = "{\"items\":{\"0\":0},\"value\":10}";
    internal const string MissingAfter = "{\"details\":{\"missing\":{\"value\":5}},\"value\":11}";
    internal const string ArrayAfter = "{\"items\":{\"0\":5},\"value\":11}";
    internal const string MissingPath = "/details/missing/value";
    internal const string ArrayPath = "/items/0";
    internal const string MissingDetail = "A patch parent is missing or is not an object.";
    internal const string ArrayDetail = "Array patch operations are not supported in the current protocol.";
}
