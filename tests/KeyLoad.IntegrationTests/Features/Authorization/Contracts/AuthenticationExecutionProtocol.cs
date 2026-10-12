namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AuthenticationExecutionProtocol
{
    internal const int MaximumEvents = 4096;
    internal const int NoEvents = 0;
    internal const int One = 1;
    internal const int SecondEpoch = 2;
    internal const int RepairedEpoch = 3;
    internal const int FinalEpoch = 5;
    internal const string PrincipalPrefix = "auth-observer-reader-";
    internal const string CredentialPrefix = "auth-observer-key-";
    internal const string MissingEvent = "The actual authentication observation is incomplete.";
    internal const string UnknownSuffix = "-not-persisted";
    internal const string MalformedScheme = "Basic ";
    internal const string DocumentsRoute = "/v1/documents/get";
}
