namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventMessageSensitiveReplayProtocol
{
    internal const string Root = "root";
    internal const string Worker = "sensitive-worker";
    internal const string Resource = "sensitive-input";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Group = "original-group";
    internal const string FreshGroup = "healthy-group";
    internal const string GrantBefore = "sensitive.read.before";
    internal const string GrantAfter = "sensitive.read.after";
    internal const string SecretPath = "/secret";
    internal const string Classification = "private-sensitive";
    internal const string EventType = "SensitiveInput";
    internal const string Payload = "{\"public\":{\"items\":[1,null,2]},\"secret\":\"SENSITIVE_BODY_NATIVE_CANARY\"}";
    internal const string Headers = "{\"public\":\"correlation\",\"secret\":\"SENSITIVE_HEADER_NATIVE_CANARY\"}";
    internal const string RedactedPayload = "{\"public\":{\"items\":[1,null,2]}}";
    internal const string RedactedHeaders = "{\"public\":\"correlation\"}";
    internal const string MissingPath = "/absent";
    internal const string SafePayload = "{\"items\":[null,2,1],\"public\":\"Привіт\"}";
    internal const string EmptyHeaders = "{}";
    internal const long RevokedEpoch = 2;
    internal const long RepairedEpoch = 3;
    internal const long SchemaVersionStep = 1;
}
