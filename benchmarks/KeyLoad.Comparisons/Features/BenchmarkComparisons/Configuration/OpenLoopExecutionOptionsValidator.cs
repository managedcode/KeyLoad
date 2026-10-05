using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Rejects any options that differ from the qualified open-loop execution policy.</summary>
public sealed class OpenLoopExecutionOptionsValidator : IValidateOptions<OpenLoopExecutionOptions>
{
    private const string ValidationFailureMessage = "Open-loop execution policy must match the qualified v1 cohort.";

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenLoopExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return IsExact(options) ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(ValidationFailureMessage);
    }

    private static bool IsExact(OpenLoopExecutionOptions options)
        => options.QueueCapacity == OpenLoopExecutionOptions.DefaultQueueCapacity
            && options.ConcurrentSessions == OpenLoopExecutionOptions.DefaultConcurrentSessions
            && options.MaximumNodes == OpenLoopExecutionOptions.DefaultMaximumNodes
            && options.OperationDeadlineMilliseconds == OpenLoopExecutionOptions.DefaultOperationDeadlineMilliseconds
            && options.DrainMilliseconds == OpenLoopExecutionOptions.DefaultDrainMilliseconds
            && options.ControlPollMilliseconds == OpenLoopExecutionOptions.DefaultControlPollMilliseconds
            && options.SpinWindowMicroseconds == OpenLoopExecutionOptions.DefaultSpinWindowMicroseconds;
}
