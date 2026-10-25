namespace KeyLoad.ClusterTests.Authorization;

internal static class SensitiveAuthorityProtocol
{
    internal const string RedactedJson = "{\"public\":\"Привіт é 😀 \\\"quoted\\\"\"}";
    internal const string PatchedJson = "{\"public\":\"Привіт é 😀 \\\"quoted\\\"\",\"secret\":\"mcp-conflicting-value-canary\"}";
    internal const string RawRead = "pii.read";
    internal const string RawWrite = "pii.write";
    internal const string PatchKindName = "patchDocument";
    internal const string ColdScenario = "sensitive-repair-cold";
    internal const int RepairedEpoch = 2;
    internal const long PatchedRevision = 2;
}
