namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class CommittedResponseProtocol
{
    internal const string ApiKeyId = "committed-response-key";
    internal const string ApiKey = "committed-response-test-credential-32-characters";
    internal const string Bearer = "Bearer ";
    internal const string Authorization = "Authorization";
    internal const string CommandHeader = "X-KeyLoad-Command-Id";
    internal const string JsonMediaType = "application/json";
    internal const string Document = "committed-response-original";
    internal const string OriginalJson = "{\"value\":1}";
    internal const string ChangedJson = "{\"value\":99}";
    internal const string HealthyJson = "{\"value\":2}";
    internal const string ConflictDetail = "The command ID was already used with different content.";
    internal const string UnknownDetail = "The write response is unavailable. Retry the same command ID.";
    internal const string UnexpectedRequest = "The original bounded network/native operation differs.";
    internal const string MutationKind = "putDocument";
    internal const int InitialIndex = 0;
    internal const int ReplayIndex = 1;
    internal const int HealthyIndex = 2;
    internal const int HealthyReplayIndex = 3;
    internal const int SuccessfulRequests = 4;
    internal const int FirstRequest = 1;
    internal const int MaximumRequests = 5;
    internal const int FirstChunkBytes = 1;
    internal const long AbsentRevision = 0;
    internal const long FirstRevision = 1;
    internal const long SecondRevision = 2;
    internal const long InitialEpoch = 1;
    internal const long CommitAdvance = 1;
}
