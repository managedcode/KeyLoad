namespace KeyLoad.Server;

internal static class ServerProtocol
{
    internal const string ConfigurationSection = "KeyLoad";
    internal const string HealthPrefix = "/health";
    internal const string InternalPrefix = "/internal";
    internal const string ReadyPath = "/health/ready";
    internal const string ReadyStatus = "ready";
    internal const string PrincipalItem = "keyload-principal";
    internal const string ExecutionRequestItem = "keyload-execution-request";
    internal const string CommandHeader = "X-KeyLoad-Command-Id";
    internal const string RequestHeader = "X-KeyLoad-Request-Id";
    internal const string NonceHeader = "X-KeyLoad-Nonce";
    internal const string BearerPrefix = "Bearer ";
    internal const string BinaryContentType = "application/octet-stream";
    internal const string JsonContentType = "application/json; charset=utf-8";
    internal const string MissingCredential = "An API key is required.";
    internal const string StableCommandRequired = "A stable X-KeyLoad-Command-Id header is required.";
    internal const string BodyExceeded = "The request body exceeds its byte budget.";
    internal const string InvalidJson = "The request contains invalid protocol JSON.";
    internal const string RequestCancelled = "The database request was cancelled.";
    internal const int KestrelMaximumBodyBytes = 33_554_432;
    internal const int MaximumJsonDepth = 64;
    private const string JsonNullLiteral = "null";
    internal static readonly byte[] NullPayload = System.Text.Encoding.UTF8.GetBytes(JsonNullLiteral);
}
