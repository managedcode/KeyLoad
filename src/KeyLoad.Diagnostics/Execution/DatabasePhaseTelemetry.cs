namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Owns the optional physical-process bank, configured before database work.</summary>
public static class DatabasePhaseTelemetry
{
    private const string ConflictingMode = "Database phase profiling mode was already initialized differently.";
    private static readonly Lock InitializationGate = new();
    private static DatabasePhaseBank? bank;

    /// <summary>Initializes once before hosts open; repeated identical setup is idempotent.</summary>
    public static void Initialize(bool enabled, int stripeCount, int maximumCasAttempts, TimeProvider? timeProvider = null)
    {
        DatabasePhaseSettingsValidation.Validate(stripeCount, maximumCasAttempts);
        lock (InitializationGate)
        {
            if (bank is not null)
            {
                if (!bank.MatchesSettings(enabled, stripeCount, maximumCasAttempts, timeProvider))
                {
                    throw new InvalidOperationException(ConflictingMode);
                }

                return;
            }

            Volatile.Write(ref bank, new DatabasePhaseBank(enabled, stripeCount, maximumCasAttempts, timeProvider));
        }
    }

    /// <summary>Starts one scope, or returns the disabled sentinel without timing.</summary>
    public static long Begin() => Volatile.Read(ref bank)?.Begin() ?? DatabasePhaseBank.DisabledTimestamp;

    /// <summary>Records one actual terminal scope without observer or exporter work.</summary>
    public static void End(DatabasePhaseKind phase, DatabasePhaseOutcome outcome, long started)
        => Volatile.Read(ref bank)?.End(phase, outcome, started);

    /// <summary>Records a genuine zero-wait admission rejection.</summary>
    public static void RecordBusy(DatabasePhaseKind phase) => Volatile.Read(ref bank)?.RecordBusy(phase);

    /// <summary>Captures a detached cumulative observation on a cold caller.</summary>
    public static DatabasePhaseSnapshot Capture() => Volatile.Read(ref bank)?.Capture() ?? DatabasePhaseBank.CaptureDisabled();

    /// <summary>Classifies an observed OCE only from existing cancelled scope-local tokens.</summary>
    public static DatabasePhaseOutcome CancellationOutcome(CancellationToken incoming, CancellationToken owned = default)
        => incoming.IsCancellationRequested ? DatabasePhaseOutcome.CallerCancelled
            : owned.IsCancellationRequested ? DatabasePhaseOutcome.DeadlineOrStopping : DatabasePhaseOutcome.Faulted;
}
