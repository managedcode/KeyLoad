namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeCredentialStatusProtocol
{
    internal const string AdminPrefix = "native-status-admin-";
    internal const string GuidFormat = "N";
    internal const string CallerKeyId = "native-status-caller";
    internal const string CallerKey = CallerKeyId + ".owned-status-caller-32-characters";
    internal const string OriginalKeyId = "native-status-original";
    internal const string OriginalKey = OriginalKeyId + ".owned-status-original-32-characters";
    internal const string DeniedKeyId = "native-status-denied";
    internal const string DeniedKey = DeniedKeyId + ".owned-status-denied-32-characters";
    internal const string HealthyKeyId = "native-status-healthy";
    internal const string HealthyKey = HealthyKeyId + ".owned-status-healthy-32-characters";
    internal const string Route = "/v1/admin/api-keys";
    internal const string Authorization = "Authorization";
    internal const string Bearer = "Bearer ";
    internal const string CommandHeader = "X-KeyLoad-Command-Id";
    internal const string ContentType = "application/json";
    internal const string BooleanBody = "true";
    internal const string UnknownDetail = "The write response is unavailable. Retry the same command ID.";
    internal const string DeniedDetail = "Cluster administration is required.";
    internal const string Failure = "The native credential status contract was not observed.";
    internal const string AbsentDocument = "native-status-no-document";
    internal const int Empty = 0;
    internal const int First = 1;
    internal const int Requests = 6;
}
