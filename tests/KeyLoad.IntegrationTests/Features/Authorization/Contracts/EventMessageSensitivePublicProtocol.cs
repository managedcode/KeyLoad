namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitivePublicProtocol
{
    internal const string Queue = "sensitive-input";
    internal const string Message = "sensitive-message";
    internal const string EventType = "SensitiveInput";
    internal const string Group = "original-group";
    internal const string FreshGroup = "healthy-group";
    internal const string GrantBefore = "sensitive.read.before";
    internal const string GrantAfter = "sensitive.read.after";
    internal const string Path = "/secret";
    internal const string Classification = "private-sensitive";
    internal const string BodyCanary = "SENSITIVE_RF3_BODY_NATIVE_CANARY";
    internal const string HeaderCanary = "SENSITIVE_RF3_HEADER_NATIVE_CANARY";
    internal const string Payload = "{\"public\":{\"items\":[1,null,2]},\"secret\":\"SENSITIVE_RF3_BODY_NATIVE_CANARY\"}";
    internal const string Headers = "{\"public\":\"correlation\",\"secret\":\"SENSITIVE_RF3_HEADER_NATIVE_CANARY\"}";
    internal const string RedactedPayload = "{\"public\":{\"items\":[1,null,2]}}";
    internal const string RedactedHeaders = "{\"public\":\"correlation\"}";
    internal const int SingleAttempt = 1;
    internal const long InitialRevision = 1;
    internal const long VersionIncrement = 1;
    internal const string AttemptsExhausted = "AttemptsExhausted";
    internal const string EnqueueKind = "enqueue";
    internal const string PublishKind = "publishTopic";
    internal const long RevokedEpoch = 2;
    internal const long RepairedEpoch = 3;
}
