namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SdkResponseAdmissionProtocol
{
    internal const int PlacementVersion = 1;
    internal const long NextCommit = 1;
    internal const long FirstSequence = 1;
    internal const int FirstOrdinal = 0;
    internal const int SingleEntry = 1;
    internal const int NoEntries = 0;
    internal const string PutKind = "putDocument";
    internal const string Root = "root";
    internal const string Credential = "response-admission-credential";
    internal const string Commands = "/commands";
    internal const string Documents = "/documents/get";
    internal const string CommandHeader = "X-KeyLoad-Command-Id";
    internal const string JsonType = "application/json";
    internal const string Collection = "response-admission";
    internal const string Document = "original";
    internal const string Json = "{\"value\":\"Київ\",\"number\":1}";
    internal const int BelowMinimumJson = 1;
    internal const long InitialRevision = 0;
    internal const long CommittedRevision = 1;
    internal const string WriteUnavailable = "The write response is unavailable. Retry the same command ID.";
    internal const string ReadUnavailable = "The read response is unavailable.";
}
