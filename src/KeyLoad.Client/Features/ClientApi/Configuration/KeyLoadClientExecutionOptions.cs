using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Bounds retained HTTP problem bodies in the native SDK transport.</summary>
[ConfigurationOptions]
public sealed class KeyLoadClientExecutionOptions
{
    /// <summary>The SDK execution configuration section.</summary>
    public const string SectionName = "KeyLoad:ClientExecution";
    /// <summary>The rejection for an invalid retained response budget.</summary>
    public const string ValidationMessage = "The SDK problem body budget must be positive and at most 64 KiB.";
    private const int DefaultProblemBodyBytes = 65_536;
    private const int MinimumProblemBodyBytes = 1;
    private const int DefaultSqlInspectionBytes = 65_536;
    private const int DefaultSqlInspectionDepth = 32;
    private const int DefaultSqlBudgetCheckInterval = 256;

    /// <summary>The maximum UTF-8 bytes retained when decoding one HTTP problem.</summary>
    public int MaximumProblemBodyBytes { get; set; } = DefaultProblemBodyBytes;
    /// <summary>The byte ceiling for conservative local SQL write classification.</summary>
    public int MaximumSqlInspectionBytes { get; set; } = DefaultSqlInspectionBytes;
    /// <summary>The nesting ceiling for conservative local SQL trivia inspection.</summary>
    public int MaximumSqlInspectionDepth { get; set; } = DefaultSqlInspectionDepth;
    /// <summary>The SQL trivia work cadence and conservative pre-cancellation prefix width.</summary>
    public int SqlBudgetCheckInterval { get; set; } = DefaultSqlBudgetCheckInterval;

    /// <summary>Checks the positive budget against its existing protocol ceiling.</summary>
    /// <returns>Whether the SDK can consume this budget.</returns>
    public bool IsValid() => MaximumProblemBodyBytes is >= MinimumProblemBodyBytes and <= DefaultProblemBodyBytes
        && MaximumSqlInspectionBytes is >= MinimumProblemBodyBytes and <= DefaultSqlInspectionBytes
        && MaximumSqlInspectionDepth is >= MinimumProblemBodyBytes and <= DefaultSqlInspectionDepth
        && SqlBudgetCheckInterval is >= MinimumProblemBodyBytes and <= DefaultSqlBudgetCheckInterval;

    /// <summary>Maps the validated SDK snapshot to the selected parser's local inspection policy.</summary>
    /// <param name="configured">The original centrally snapshotted SDK execution bounds.</param>
    /// <returns>Validated native parser limits with the unchanged client byte and nesting ceilings.</returns>
    [ConfigurationBinding]
    internal static IOptions<DatabaseLimits> SqlInspection(KeyLoadClientExecutionOptions configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        configured.Validate();
        var limits = new DatabaseLimits
        {
            MaxQueryBytes = configured.MaximumSqlInspectionBytes,
            MaxQueryDepth = configured.MaximumSqlInspectionDepth,
            MaxQueryTokens = configured.MaximumSqlInspectionBytes
        };
        limits.Validate();
        return Options.Create(limits);
    }

    /// <summary>Captures and validates the caller's execution values for one client lifetime.</summary>
    [ConfigurationBinding]
    internal static IOptions<KeyLoadClientExecutionOptions> Snapshot(IOptions<KeyLoadClientExecutionOptions> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var value = configured.Value;
        var copy = new KeyLoadClientExecutionOptions
        {
            MaximumProblemBodyBytes = value.MaximumProblemBodyBytes,
            MaximumSqlInspectionBytes = value.MaximumSqlInspectionBytes,
            MaximumSqlInspectionDepth = value.MaximumSqlInspectionDepth,
            SqlBudgetCheckInterval = value.SqlBudgetCheckInterval
        };
        copy.Validate();
        return Options.Create(copy);
    }

    /// <summary>Rejects invalid standalone composition before the client can send a request.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(KeyLoadClientExecutionOptions), [ValidationMessage]);
        }
    }
}
