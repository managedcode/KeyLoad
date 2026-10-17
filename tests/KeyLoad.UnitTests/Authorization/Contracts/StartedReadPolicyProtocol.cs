namespace KeyLoad.UnitTests.Features.Authorization;

internal static class StartedReadPolicyProtocol
{
    internal const string StartedEventType = "cqrs-started";
    internal const string Revoked = "revoked";
    internal const string Expired = "expired";
    internal const string Cancelled = "cancelled";
    internal const string Document = "started-current-policy";
    internal const string Put = "putDocument";
    internal const string CancellationDetail = "The database request was cancelled.";
    internal const string Unavailable = "The credential is unavailable or expired.";
    internal const int EmptyFrames = 0;
    internal const int StartedFrames = 1;
    internal const int FailedFrames = 2;
    internal const long StartedSequence = 1;
    internal const long FailedSequence = 2;
    internal const long Revision = 1;
    internal const long PlacementEpoch = 1;
    internal const long RefusedEpoch = 2;
    internal const long RepairedEpoch = 3;
    internal const long EmptyPosition = 0;
}
