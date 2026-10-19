namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Obtains the launch owner's actual exit status from its execution Aspire owner.</summary>
internal static class ProjectProcessExitObservation
{
    private const int SuccessfulExit = 0;
    private const string UnexpectedSuccessfulExit = "The deliberately killed execution launch owner reported a successful exit.";

    internal static async Task<int> ReadAsync(ResourceNotificationService notifications,
        ProcessFaultCommandProtocol.Identity original, CancellationToken token)
    {
        var terminal = await notifications.WaitForResourceAsync(original.ResourceName, update =>
            update.Resource is ProjectResource && update.Resource.Name == original.ResourceName
            && update.Snapshot.StartTimeStamp?.ToUniversalTime().Ticks == original.AspireStartTimeUtcTicks
            && update.Snapshot.ExitCode is not null, token).ConfigureAwait(false);
        var code = terminal.Snapshot.ExitCode!.Value;
        if (code == SuccessfulExit)
        {
            throw new InvalidDataException(UnexpectedSuccessfulExit);
        }
        return code;
    }
}
