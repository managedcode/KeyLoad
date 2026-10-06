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

    /// <summary>Rejects invalid standalone composition before the client can send a request.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(KeyLoadClientExecutionOptions), [ValidationMessage]);
        }
    }
}
