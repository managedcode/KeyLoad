namespace KeyLoad.FunctionalTests.Authorization;

internal static class AuthorizationCredentialHttpExportProtocol
{
    internal const string RootPrincipal = "root";
    internal static readonly TimeSpan ExpiredOffset = TimeSpan.FromMinutes(1);
    internal const string MissingOriginalCredential = "Original persisted credential is missing.";
    internal const string MissingAcknowledgedCredential = "Persisted credential is missing after acknowledged configuration.";
    internal const string ExpectedProblem = """
        {"type":"urn:keyload:error:Unauthenticated","title":"Unauthenticated","status":401,"detail":"The credential is unavailable or expired.","errorCode":"Unauthenticated"}
        """;
}
