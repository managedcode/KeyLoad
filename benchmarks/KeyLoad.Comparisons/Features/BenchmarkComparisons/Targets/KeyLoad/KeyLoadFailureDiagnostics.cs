using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadFailureDiagnostics
{
    private const string FailureCode = "KeyLoad:ResourceExhausted";
    private const string SetupPrefix = "setup:";
    private static readonly TimeSpan ObservationDeadline = TimeSpan.FromSeconds(2);

    internal static bool IsEligible(ComparisonCase failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        if (!string.Equals(failed.Status, ComparisonStatuses.Failed, StringComparison.Ordinal)
            || string.Equals(failed.Detail, ComparisonSessionCleanup.Failure, StringComparison.Ordinal)
            || !Enum.IsDefined(failed.Scenario) || failed.Repetition < 0)
        {
            return false;
        }

        if (failed.Detail is { } detail
            && (string.Equals(detail, FailureCode, StringComparison.Ordinal)
                || string.Equals(detail, SetupPrefix + FailureCode, StringComparison.Ordinal)))
        {
            return true;
        }

        return failed.Measurement is not null && !failed.Samples.IsDefault
            && failed.Samples.Any(sample => sample is { Success: false, Error: FailureCode });
    }

    internal static bool IsRecoverableObservationFailure(Exception error)
        => error is OperationCanceledException or global::System.Net.Http.HttpRequestException or IOException or InvalidOperationException
            or ArgumentException or NotSupportedException or JsonException or FormatException;

    internal static void Write(string line)
    {
        try
        {
            Console.Error.WriteLine(line);
        }
        catch (Exception error) when (IsRecoverableOutputFailure(error))
        {
            // Broken or unavailable diagnostic output must not replace the settled case.
        }
    }

    internal static bool IsRecoverableOutputFailure(Exception error)
        => error is IOException or InvalidOperationException or ArgumentException or NotSupportedException;

    internal static TimeSpan Deadline => ObservationDeadline;

    internal static async Task ObserveAsync(global::KeyLoad.Client.KeyLoadClient client,
        global::KeyLoad.PartitionRef partition, ComparisonCase failed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(failed);
        if (!IsEligible(failed))
        {
            return;
        }

        var line = KeyLoadOutboxDiagnosticLine.UnavailableLine;
        if (!cancellationToken.IsCancellationRequested)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Deadline);
            try
            {
                var result = await client.OutboxStatusAsync(partition, deadline.Token).ConfigureAwait(false);
                if (result.IsSuccess && result.Value is { } status)
                {
                    line = KeyLoadOutboxDiagnosticLine.Format(failed, status);
                }
            }
            catch (Exception error) when (IsRecoverableObservationFailure(error))
            {
                // Observation errors are private and cannot replace the original failed case.
            }
        }

        Write(line);
    }
}

public sealed partial class KeyLoadTarget : IComparisonFailureDiagnostics
{
    /// <inheritdoc />
    Task IComparisonFailureDiagnostics.ObserveFailureAsync(ComparisonCase failed, CancellationToken cancellationToken)
        => KeyLoadFailureDiagnostics.ObserveAsync(client, partition, failed, cancellationToken);
}
