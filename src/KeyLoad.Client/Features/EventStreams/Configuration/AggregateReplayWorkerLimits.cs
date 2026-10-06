using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Bounds a pure client replay worker independently of server request limits.</summary>
[ConfigurationOptions]
public sealed record AggregateReplayWorkerLimits
{
    private const int MinimumPositiveBudget = 0;
    private const int MaximumEventCeiling = 65_536;
    private const int MaximumStateByteCeiling = 16_777_216;
    private const int MaximumInputByteCeiling = 67_108_864;
    private const int MaximumJsonDepthCeiling = 64;
    private const int MaximumRegisteredUpcasterCeiling = 64;
    /// <summary>Canonical caller configuration section.</summary>
    public const string SectionName = "KeyLoad:AggregateReplayWorker";
    /// <summary>Validation failure retained by replay workers and native binding.</summary>
    public const string ValidationMessage = "Replay worker limits must be positive and within their hard ceilings.";
    /// <summary>Maximum number of event records accepted by one worker invocation.</summary>
    public int MaximumEvents { get; init; } = 4096;

    /// <summary>Maximum UTF8 bytes in any intermediate state value.</summary>
    public int MaximumStateBytes { get; init; } = 1_048_576;

    /// <summary>Maximum combined original state, payload and header UTF8 bytes.</summary>
    public int MaximumInputBytes { get; init; } = 16_777_216;

    /// <summary>Maximum JSON nesting depth accepted for state, payloads and headers.</summary>
    public int MaximumJsonDepth { get; init; } = 64;

    /// <summary>Maximum registered one-version event transforms.</summary>
    public int MaximumRegisteredUpcasters { get; init; } = 64;

    /// <summary>Checks the configured budgets against the existing replay ceilings.</summary>
    /// <returns>Whether the worker can safely execute within these budgets.</returns>
    public bool IsValid() => MaximumEvents is > MinimumPositiveBudget and <= MaximumEventCeiling &&
        MaximumStateBytes is > MinimumPositiveBudget and <= MaximumStateByteCeiling &&
        MaximumInputBytes is > MinimumPositiveBudget and <= MaximumInputByteCeiling &&
        MaximumJsonDepth is > MinimumPositiveBudget and <= MaximumJsonDepthCeiling &&
        MaximumRegisteredUpcasters is > MinimumPositiveBudget and <= MaximumRegisteredUpcasterCeiling;

    /// <summary>Rejects invalid standalone settings before caller execution.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(AggregateReplayWorkerLimits), [ValidationMessage]);
        }
    }
}
