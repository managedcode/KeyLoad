namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Named genuine document scenario values and public expected outcome bounds.</summary>
internal static class McpDocumentProtocol
{
    internal const string TenantPrefix = "mcp-tenant-";
    internal const string Database = "mcp-database";
    internal const string Domain = "mcp-documents";
    internal const string Collection = "mcp-documents";
    internal const string Entity = "document-1";
    internal const string PrivateValue = "mcp-private-value-canary";
    internal const string ConflictValue = "mcp-conflicting-value-canary";
    internal const string InitialJson = "{\"public\":\"Привіт é 😀 \\\"quoted\\\"\",\"secret\":\"mcp-private-value-canary\"}";
    internal const string ConflictingJson = "{\"public\":\"changed\",\"secret\":\"mcp-conflicting-value-canary\"}";
    internal const string ProtectedPath = "/secret";
    internal const string ProtectedClassification = "mcp-sensitive";
    internal const string ProtectedPatchJson = "\"mcp-conflicting-value-canary\"";
    internal const int ExpectedAbsentRevision = 0;
    internal const int ObservedOperationCount = 3;
}
