namespace KeyLoad.Features.InternalSerialization;

internal static class NativePayloadVersion
{
    internal const uint Current = 1;
    internal const string TestAssembly = "KeyLoad.UnitTests";
    internal const string ReplicationAssembly = "KeyLoad.Replication";
    internal const string Alias = "keyload.internal.value.v1";
    internal const string InvalidPayload = "The internal binary payload is invalid.";
    internal const string UnsupportedVersion = "The internal binary payload version is unsupported.";
}
