using KeyLoad.ProcessRecoveryTests.DocumentStorage;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

[ConfigurationOptions]
internal sealed class SharedDomainProcessExecutionOptions
{
    private const string Invalid = "Shared-domain process recovery must remain within the original execution execution and cleanup policy.";
    internal TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds);
    internal TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds);

    internal void Validate()
    {
        if (ExecutionTimeout <= TimeSpan.Zero || ExecutionTimeout.TotalSeconds > CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds
            || CleanupTimeout <= TimeSpan.Zero || CleanupTimeout.TotalSeconds > CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds)
        {
            throw new InvalidOperationException(Invalid);
        }
    }
}
