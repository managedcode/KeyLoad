using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Reads the selected execution Aspire process without accepting a caller-supplied process owner.</summary>
internal static class ProjectProcessIdentity
{
    private const int MinimumProcessId = 1;

    internal static ProcessFaultCommandProtocol.LaunchIdentity ReadLaunch(ResourceNotificationService notifications, string resourceName)
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

    internal static async Task<ProcessFaultCommandProtocol.Identity> ReadAsync(ResourceNotificationService notifications,
        string resourceName, int serverProcessId, IOptions<TestExecutionOptions> options, CancellationToken token)
    {
        var launch = ReadLaunch(notifications, resourceName);
        using var server = Process.GetProcessById(serverProcessId);
        var started = server.StartTime.ToUniversalTime().Ticks;
        if (server.HasExited || started < launch.ProcessStartTimeUtcTicks)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        await ProjectProcessParent.RequireOwnedAsync(serverProcessId, launch.ProcessId, options, token).ConfigureAwait(false);
        if (server.HasExited || server.StartTime.ToUniversalTime().Ticks != started
            || ReadLaunch(notifications, resourceName) != launch)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        return new(resourceName, serverProcessId, launch.AspireStartTimeUtcTicks, started,
            launch.ProcessId, launch.ProcessStartTimeUtcTicks);
    }
}
