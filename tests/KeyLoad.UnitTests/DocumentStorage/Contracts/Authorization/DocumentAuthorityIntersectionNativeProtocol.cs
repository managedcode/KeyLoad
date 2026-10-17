namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentAuthorityIntersectionNativeProtocol
{
    internal const long SourceEpoch = 2;
    internal const long TargetEpoch = 4;
    internal const long EpochStep = 1;
    internal const long Revision = 1;
    internal const string SourceRead = "document.source.raw";
    internal const string TargetRead = "document.target.raw";
    internal const string SourceSchema = "document.source.schema";
    internal const string TargetSchema = "document.target.schema";
    internal const string Classification = "private";
    internal const string SecretPath = "/secret";
    internal const string FieldId = "authority-fields";
    internal const string FieldJson = "{\"public\":1,\"secret\":\"native-private-canary\"}";
    internal const string FieldSafe = "{\"public\":1}";
    internal const string RowJson = "{\"visible\":true}";
    internal const string Missing = "The original native document authority setup failed.";
}
