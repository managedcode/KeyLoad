namespace KeyLoad.InternalSerialization;

internal static class PayloadVersion
{
    internal const uint Current = 1;
    internal const string TestAssembly = "KeyLoad.FunctionalTests";
    internal const string Alias = "keyload.internal.value";
    internal const string InvalidPayload = "The internal binary payload is invalid.";
    internal const string UnsupportedVersion = "The internal binary payload version is unsupported.";
}
