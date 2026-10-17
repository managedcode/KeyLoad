namespace KeyLoad.UnitTests.Features.Authorization;

internal static class FieldWriteOutputProtocol
{
    internal const string Collection = "field-write-output";
    internal const string Document = "original";
    internal const string Writer = "field-only-writer";
    internal const string Reader = "raw-only-reader";
    internal const string Root = "root";
    internal const string Path = "/profile/secret~0~1";
    internal const string ParentPath = "/profile";
    internal const string ReadGrant = "field.output.read";
    internal const string WriteGrant = "field.input.write";
    internal const string UseGrant = "field.predicate.use";
    internal const string Classification = "private";
    internal const string Original = """{"profile":{"public":"visible","secret~/":"original-canary"},"status":"open"}""";
    internal const string Updated = """{"profile":{"public":"visible","secret~/":"updated-canary"},"status":"open"}""";
    internal const string Hidden = """{"profile":{"public":"visible"},"status":"open"}""";
    internal const string Value = "\"updated-canary\"";
    internal const string ParentValue = """{"public":"visible","secret~/":"updated-canary"}""";
    internal const string Denied = "The mutation requires a protected field-write grant.";
    internal const string ChangedPolicy = "The principal policy changed since this command was evaluated.";
    internal const string Put = "putDocument";
    internal const string Patch = "patchDocument";
    internal const long OriginalRevision = 1;
    internal const long CurrentRevision = 2;
    internal const long RepairedEpoch = 2;
    internal const long OwnershipEpoch = 1;
    internal const long NextPosition = 1;
    internal const int ColdCount = 2;
}
