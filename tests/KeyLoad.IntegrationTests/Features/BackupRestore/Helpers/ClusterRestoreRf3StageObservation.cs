using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Fixture-owned observation only; the marker does not prove a database stage.</summary>
internal sealed class ClusterRestoreRf3StageObservation
{
    private readonly Guid operationId;
    private readonly NativeClusterRestoreStage stage;
    private readonly int maximumCharacters;
    private readonly TaskCompletionSource<int> observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task reader;
    private int characters;

    internal ClusterRestoreRf3StageObservation(ResourceLoggerService logs, Guid operationId,
        NativeClusterRestoreStage stage, int maximumCharacters, CancellationToken cancellationToken)
    {
        this.operationId = operationId;
        this.stage = stage;
        this.maximumCharacters = maximumCharacters;
        reader = ReadAsync(logs, cancellationToken);
    }

    internal async Task<int> WaitAsync(CancellationToken cancellationToken)
    {
        var settled = await Task.WhenAny(observed.Task, reader).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (ReferenceEquals(settled, reader))
        {
            await reader.ConfigureAwait(false);
            if (!observed.Task.IsCompletedSuccessfully)
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        }
        return await observed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal Task JoinAsync() => reader;

    private async Task ReadAsync(ResourceLoggerService logs, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var owned = logs.WatchAsync(ClusterRestoreRf3Protocol.RestoreResource).GetAsyncEnumerator(cancellationToken);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            while (await owned.MoveNextAsync().ConfigureAwait(false))
            {
                foreach (var line in owned.Current)
                { Observe(line); }
            }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Observe(LogLine line)
    {
        if (line.Content.Length > maximumCharacters - characters)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        characters += line.Content.Length;
        if (!line.Content.StartsWith(ClusterRestoreRf3ResumeProtocol.Marker + ClusterRestoreRf3ResumeProtocol.Separator,
            StringComparison.Ordinal))
        { return; }
        var fields = line.Content.Split(ClusterRestoreRf3ResumeProtocol.Separator);
        if (line.IsErrorMessage || fields.Length != ClusterRestoreRf3ResumeProtocol.MarkerFields
            || !string.Equals(fields[ClusterRestoreRf3ResumeProtocol.StageField], stage.ToString(), StringComparison.Ordinal)
            || !string.Equals(fields[ClusterRestoreRf3ResumeProtocol.OperationField], operationId.ToString(ClusterRestoreRf3Protocol.IdentityFormat), StringComparison.Ordinal)
            || !int.TryParse(fields[ClusterRestoreRf3ResumeProtocol.ProcessField], NumberStyles.None,
                CultureInfo.InvariantCulture, out var pid) || pid < ClusterRestoreRf3ResumeProtocol.MinimumPid
            || !observed.TrySetResult(pid))
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
    }
}
