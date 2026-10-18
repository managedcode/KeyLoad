namespace KeyLoad.Clustering;

/// <summary>Holds publication completion state under the original request owner gate.</summary>
internal struct NativeRequestWorkLeasePublication
{
    internal bool Completing
    {
        get; private set;
    }

    internal Task BeginCompletion()
    {
        Completing = true;
        return Task.CompletedTask;
    }

    internal readonly void RequirePublicationOpen()
    {
        if (Completing)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.AdmissionClosedMessage);
        }
    }
}
