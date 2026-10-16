namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Joins the same matching original producers and admitted publication work without renewing their lifetime.</summary>
internal interface IResourceLifetimeWorkSettlement
{
    Task CancelAndJoinAsync();
}
