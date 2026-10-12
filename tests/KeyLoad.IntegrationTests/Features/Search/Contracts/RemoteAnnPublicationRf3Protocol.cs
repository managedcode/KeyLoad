namespace KeyLoad.IntegrationTests.Features.Search;

internal static class RemoteAnnPublicationRf3Protocol
{
    internal const int Version = 1;
    internal const int EmptyCount = 0;
    internal const int Count = 3;
    internal const long FirstEpoch = 1;
    internal const long DeniedEpoch = 2;
    internal const long RepairedEpoch = 3;
    internal const long FieldDeniedEpoch = 4;
    internal const long FieldRepairedEpoch = 5;
    internal const string Classification = "private";
    internal const string RawRead = "ann.remote.read";
    internal const string RawUse = "ann.remote.use";
    internal const string FieldWrite = "ann.remote.write";
    internal const string ReaderPrefix = "ann-remote-reader-";
    internal const string CredentialPrefix = "ann-remote-key-";
    internal const string CredentialSeparator = ".";
    internal const int SecretBytes = 32;
    internal const string IdentityFormat = "N";
    internal const string MovedConsumer = "ann-remote-moved-consumer";
    internal const string InvalidOwner = "Actual ANN publication owner identity was not preserved.";
}
