namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SuccessBodyProtocol
{
    internal const string NullBody = "null";
    internal const string MalformedBody = "{";
    internal const string KeyId = "execution-success-body-key";
    internal const string ApiKey = KeyId + ".execution-success-body-credential-32-characters";
    internal const string ChangedJson = "{\"execution\":\"changed\",\"value\":99}";
    internal const string HealthyJson = "{\"execution\":\"Львів\",\"value\":2}";
    internal const string ConflictDetail = "The command ID was already used with different content.";
    internal const string Failure = "The original complete execution response contract was not observed.";
    internal const int RequestCount = 8;
    internal const int FirstOrdinal = 0;
    internal const int FirstStep = 1;
    internal const int OriginalEntry = 0;
    internal const int HealthyEntry = 1;
    internal const int TwoEntries = 2;
    internal const long HealthyRevision = 2;
}
