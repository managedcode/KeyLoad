using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Holds publication and completion-observer state under the original request owner gate.</summary>
internal struct NativeRequestWorkLeasePublication
{
    private const int NoObservers = 0;
    private const int SingleObserver = 1;
    private TaskCompletionSource? cancelObservers;
    private int observerCount;
    private bool publicationClosed;

    internal bool Completing
    {
        get; private set;
    }

    internal bool ClosePublication(bool retainCancellation)
    {
        publicationClosed = true;
        if (Completing || !retainCancellation)
        {
            return false;
        }
        if (observerCount == NoObservers)
        {
            cancelObservers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        observerCount = checked(observerCount + SingleObserver);
        return true;
    }

    internal void RetainCompletionObserver()
    {
        RequirePublicationOpen();
        if (observerCount == NoObservers)
        {
            cancelObservers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        observerCount = checked(observerCount + SingleObserver);
    }

    internal Task BeginCompletion()
    {
        Completing = true;
        publicationClosed = true;
        return cancelObservers?.Task ?? Task.CompletedTask;
    }

    internal TaskCompletionSource? ReleaseCloseObserver()
        => --observerCount == NoObservers ? cancelObservers : null;

    internal readonly void RequirePublicationOpen()
    {
        if (publicationClosed || Completing)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.AdmissionClosedMessage);
        }
    }
}
