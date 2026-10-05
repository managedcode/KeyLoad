namespace KeyLoad.Orleans;

/// <summary>Defines closed admission bounds and safe failures for local request work.</summary>
internal static class NativeRequestWorkLimits
{
    internal const string InvalidIdentityMessage = "Native request work identity is invalid.";
    internal const string CapacityMessage = "Native request work capacity is exhausted.";
    internal const string AdmissionClosedMessage = "Native request work admission is closed.";
    internal const string LeaseIdentityMessage = "Native request work lease identity is invalid.";
}
