namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class DeclaredResponseEofProtocol
{
    internal const string KeyId = "declared-eof-key";
    internal const string ApiKey = KeyId + ".declared-eof-native-test-credential-32-characters";
    internal const string Authorization = "Authorization";
    internal const string Bearer = "Bearer ";
    internal const string CommandHeader = "X-KeyLoad-Command-Id";
    internal const string Commands = "/v1/commands";
    internal const string Documents = "/v1/documents/get";
    internal const string ContentType = "application/json";
    internal const string Document = "declared-eof-original";
    internal const string OriginalJson = "{\"native\":\"Київ\",\"value\":1}";
    internal const string PutKind = "putDocument";
    internal const string Root = "root";
    internal const string Failure = "The original declared response operation did not reach its native boundary.";
    internal const string WriteDetail = "The write response is unavailable. Retry the same command ID.";
    internal const string ReadDetail = "The read response is unavailable.";
    internal const int PrefixBytes = 1;
    internal const int FirstRequest = 1;
    internal const int RequestsPerRoute = 2;
    internal const int OriginalOrdinal = 0;
    internal const int OneEntry = 1;
    internal const int Empty = 0;
    internal const int FirstSequence = 1;
    internal const int PlacementVersion = 1;
    internal const long Revision = 1;
    internal const long AbsentRevision = 0;
}
