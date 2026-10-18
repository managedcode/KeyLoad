using System.Diagnostics;
using System.Globalization;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Reads the selected native Aspire process without accepting a caller-supplied process owner.</summary>
internal static class ProjectProcessIdentity
{
    private const int MinimumProcessId = 1;

    internal static ProcessFaultCommandProtocol.Identity Read(ResourceNotificationService notifications, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        if (!notifications.TryGetCurrentState(resourceName, out var current)
            || current.Resource is not ProjectResource
            || current.Resource.Name != resourceName
            || current.Snapshot.State?.Text != KnownResourceStates.Running
            || current.Snapshot.StartTimeStamp is not { } started)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        var value = current.Snapshot.Properties.Single(property => property.Name == ProcessFaultCommandProtocol.ProcessIdProperty).Value;
        if (!int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None,
                CultureInfo.InvariantCulture, out var processId) || processId < MinimumProcessId)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        using var process = Process.GetProcessById(processId);
        if (process.HasExited)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        return new(resourceName, processId, started.ToUniversalTime().Ticks, process.StartTime.ToUniversalTime().Ticks);
    }
}
