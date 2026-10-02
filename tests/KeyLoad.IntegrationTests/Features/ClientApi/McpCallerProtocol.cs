namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Named public wire and qualification bounds for genuine official MCP callers.</summary>
internal static class McpCallerProtocol
{
    internal const string FixtureKey = "rf3";
    internal const string Node1 = "node1";
    internal const string Node2 = "node2";
    internal const string Node3 = "node3";
    internal const string HttpEndpoint = "http";
    internal const string Endpoint = "/mcp";
    internal const string ProtocolVersion = "2026-07-28";
    internal const string AuthorizationHeader = "Authorization";
    internal const string BearerPrefix = "Bearer ";
    internal const string BearerScheme = "Bearer";
    internal const string RequestHeader = "X-KeyLoad-Request-Id";
    internal const string GuidFormat = "N";
    internal const string Request = "request";
    internal const string CommandId = "commandId";
    internal const string Result = "result";
    internal const string Error = "error";
    internal const string RequestId = "requestId";
    internal const string PrincipalId = "principalId";
    internal const string Roles = "roles";
    internal const string AdministratorRole = "ClusterAdministrator";
    internal const string UnconfiguredCredentialPrefix = "unconfigured-mcp-key.";
    internal const string MissingClient = "The genuine MCP client has not connected.";
    internal const string MissingEndpoint = "The Aspire HTTP endpoint is unavailable.";
    internal const string MissingStructuredResult = "The native tool result has no structured envelope.";
    internal const string ProblemType = "type";
    internal const string ProblemTitle = "title";
    internal const string ProblemStatus = "status";
    internal const string ProblemDetail = "detail";
    internal const string ProblemCode = "errorCode";
    internal const int ToolCount = 53;
    internal const int AstVersion = 1;
    internal const int FirstRevision = 1;
    internal const int EpochIncrement = 1;
    internal const int SummaryByteLimit = 1_024;
    internal const int ErrorByteLimit = 65_536;
    internal static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan ExpiredOffset = TimeSpan.FromMinutes(1);
}
