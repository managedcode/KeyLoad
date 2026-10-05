namespace KeyLoad.Core;

/// <summary>Centrally configured bounded work for one node-local due discovery page.</summary>
[ConfigurationOptions]
public sealed record DueWorkExecutionOptions
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:DueWorkExecution";
    /// <summary>Safe startup rejection for invalid due discovery budgets.</summary>
    public const string ValidationMessage = "The due discovery deadline must be positive and at most one minute.";
    private const int DefaultDiscoveryDeadlineMilliseconds = 100;
    private const int MaximumDiscoveryDeadlineMinutes = 1;
    private static readonly TimeSpan MaximumDiscoveryDeadline = TimeSpan.FromMinutes(MaximumDiscoveryDeadlineMinutes);

    /// <summary>Gets the elapsed-time budget for each bounded native due discovery page.</summary>
    public TimeSpan DiscoveryDeadline { get; init; } = TimeSpan.FromMilliseconds(DefaultDiscoveryDeadlineMilliseconds);

    /// <summary>Checks that the deadline remains positive and bounded.</summary>
    /// <returns>Whether the configured discovery deadline is valid.</returns>
    public bool IsValid() => DiscoveryDeadline > TimeSpan.Zero && DiscoveryDeadline <= MaximumDiscoveryDeadline;

    /// <summary>Rejects invalid settings before due work is admitted.</summary>
    /// <exception cref="InvalidOperationException">The configured deadline is invalid.</exception>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
