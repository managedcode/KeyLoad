namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Owns one local authenticated replica-forward request through its original queued completion.</summary>
internal interface IReplicaForwardedCommandWork
{
    ReadExecutionBudget Budget
    {
        get;
    }
    IResourceLifetimeWorkBinding Binding
    {
        get;
    }
    CancellationToken Cancellation
    {
        get;
    }
    void AdmitPublication();
    Task CancelAsync();
    Task<Exception?> CompleteAsync(Exception? original);
}
