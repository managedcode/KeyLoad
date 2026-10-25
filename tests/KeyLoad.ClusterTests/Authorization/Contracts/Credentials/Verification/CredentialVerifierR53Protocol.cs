namespace KeyLoad.ClusterTests.Authorization;

internal static class CredentialVerifierR53Protocol
{
    internal const string SecretSeparator = ".";
    internal const string SecretFormat = "N";
    internal const string ScopeDeniedDetail = "The principal cannot perform this operation in this scope.";
    internal const string UnavailableDetail = "The credential is unavailable or expired.";
    internal const string MissingState = "The original verifier replacement state is absent.";
    internal const string UnexpectedAdmission = "The unavailable credential unexpectedly initialized an official client.";
    internal const int PlacementSchema = 1;
    internal const int ExpectedUnauthorizedErrors = 1;
    internal const int FirstError = 0;
}
