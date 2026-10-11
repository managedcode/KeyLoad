namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationMalformedHttpExportProtocol
{
    internal const string JsonMediaType = "application/json";
    internal const int TruncatedClosingCharacters = 1;
    internal const string ExpectedProblem = """
        {"type":"urn:keyload:error:Validation","title":"Validation","status":400,"detail":"The request contains invalid protocol JSON.","errorCode":"Validation"}
        """;
}
