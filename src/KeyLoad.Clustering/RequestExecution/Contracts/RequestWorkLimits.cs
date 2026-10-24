namespace KeyLoad.Clustering;

/// <summary>Defines closed admission bounds and safe failures for local request work.</summary>
internal static class RequestWorkLimits
{
    internal const string InvalidIdentityMessage = "Execution request work identity is invalid.";
    internal const string DuplicateMessage = "Execution request work identity is already active.";
    internal const string AdmissionClosedMessage = "Execution request work admission is closed.";
    internal const string LeaseIdentityMessage = "Execution request work lease identity is invalid.";
}
