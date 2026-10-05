using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Bounds a pure client replay worker independently of server request limits.</summary>
[ConfigurationOptions]
public sealed record AggregateReplayWorkerLimits
{
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
    public bool IsValid() => MaximumEvents is > 0 and <= 65_536 &&
        MaximumStateBytes is > 0 and <= 16_777_216 && MaximumInputBytes is > 0 and <= 67_108_864 &&
        MaximumJsonDepth is > 0 and <= 64 && MaximumRegisteredUpcasters is > 0 and <= 64;

    /// <summary>Rejects invalid standalone settings before caller execution.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(AggregateReplayWorkerLimits), [ValidationMessage]);
        }
    }
}
