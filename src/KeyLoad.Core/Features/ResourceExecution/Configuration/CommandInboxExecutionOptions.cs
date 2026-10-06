using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Node-local command lane scheduling within the existing fairness ceiling.</summary>
[ConfigurationOptions]
public sealed record CommandInboxExecutionOptions
{
    /// <summary>The centrally bound command inbox execution section.</summary>
    public const string SectionName = "KeyLoad:CommandInboxExecution";
    /// <summary>The startup rejection for an invalid control scheduling burst.</summary>
    public const string ValidationMessage = "The command control burst must be between one and eight.";
    private const int MinimumControlBurst = 1;
    private const int DefaultMaximumControlBurst = 8;

    /// <summary>The maximum consecutive control commands before a waiting data command is read.</summary>
    public int MaximumControlBurst { get; init; } = DefaultMaximumControlBurst;

    /// <summary>Checks the original eight-command fairness ceiling.</summary>
    public bool IsValid() => MaximumControlBurst is >= MinimumControlBurst and <= DefaultMaximumControlBurst;

    /// <summary>Rejects invalid scheduling before the inbox owns a native semaphore.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(CommandInboxExecutionOptions), [ValidationMessage]);
        }
    }
}
