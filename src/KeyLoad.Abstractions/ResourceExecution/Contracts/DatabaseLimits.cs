namespace KeyLoad;

/// <summary>Defines the optional queue transfer attempt policies applied to database operations.</summary>
[ConfigurationOptions]
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.DatabaseLimits)]
public sealed record DatabaseLimits
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:DatabaseLimits";
    /// <summary>Safe startup rejection for invalid queue transfer attempt policies.</summary>
    public const string ValidationMessage = "Configured queue transfer attempt policies must be positive.";
    private const uint QueueTransferAttemptCeilingField = 20;
    private const uint QueueTransferRepairCeilingField = 21;
    private const int MinimumAttempts = 1;

    /// <summary>Optional finite source transfer Accept ceiling, including its original attempt; null disables advancement.</summary>
    [Orleans.Id(QueueTransferAttemptCeilingField)]
    public int? MaxQueueTransferAcceptAttempts
    {
        get; init;
    }

    /// <summary>Optional bounded transfer policy-repair attempts; null disables automatic repair.</summary>
    [Orleans.Id(QueueTransferRepairCeilingField)]
    public int? MaxQueueTransferRepairAttempts
    {
        get; init;
    }

    /// <summary>Checks that configured queue transfer attempt policies are positive.</summary>
    /// <returns>Whether the configured policies are valid.</returns>
    public bool IsValid() => (MaxQueueTransferAcceptAttempts is null or >= MinimumAttempts)
        && (MaxQueueTransferRepairAttempts is null or >= MinimumAttempts);

    /// <summary>Rejects invalid policies before database operations are admitted.</summary>
    /// <exception cref="InvalidOperationException">A configured queue transfer attempt policy is invalid.</exception>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
