using System.Globalization;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Operational bounds for privacy-preserving native comparison diagnostics.</summary>
[ConfigurationOptions]
public sealed class NativeComparisonDiagnosticOptions
{
    private const int MinimumPositiveLimit = 1;
    private const int DefaultKurrentSetupMaximumCauses = 3;
    private const int DefaultKurrentSetupMaximumFramesPerCause = 8;
    private const int DefaultKurrentSetupMaximumIdentifierCharacters = 64;
    private const int DefaultKurrentSetupBuilderCapacity = 4096;
    private const int DefaultKurrentCleanupMaximumExceptionDepth = 8;
    private const int DefaultKurrentCleanupMaximumCharacters = 4096;
    private const int DefaultRedisReplicaMaximumCharacters = 4096;
    private const int DefaultKeyLoadOutboxMaximumBytes = 512;
    private const int DefaultKeyLoadOutboxMaximumConsumers = 64;
    /// <summary>The centrally bound diagnostic policy section.</summary>
    public const string SectionName = "NativeComparisonDiagnostics";
    /// <summary>The unchanged diagnostic policy validation failure category.</summary>
    public const string ValidationMessage = "Native comparison diagnostic limits must be present, positive and within their supported bounds.";
    /// <summary>The number of safe exception causes retained during setup.</summary>
    public int KurrentSetupMaximumCauses { get; set; } = DefaultKurrentSetupMaximumCauses;
    /// <summary>The native stack frames retained for each setup cause.</summary>
    public int KurrentSetupMaximumFramesPerCause { get; set; } = DefaultKurrentSetupMaximumFramesPerCause;
    /// <summary>The maximum ASCII metadata identifier length.</summary>
    public int KurrentSetupMaximumIdentifierCharacters { get; set; } = DefaultKurrentSetupMaximumIdentifierCharacters;
    /// <summary>The initial setup diagnostic builder reservation, independent of truncation.</summary>
    public int KurrentSetupBuilderCapacity { get; set; } = DefaultKurrentSetupBuilderCapacity;
    /// <summary>The maximum cleanup exception chain traversal depth.</summary>
    public int KurrentCleanupMaximumExceptionDepth { get; set; } = DefaultKurrentCleanupMaximumExceptionDepth;
    /// <summary>The maximum cleanup console line length including prefix and newline.</summary>
    public int KurrentCleanupMaximumCharacters { get; set; } = DefaultKurrentCleanupMaximumCharacters;
    /// <summary>The maximum Redis replica JSON projection length.</summary>
    public int RedisReplicaMaximumCharacters { get; set; } = DefaultRedisReplicaMaximumCharacters;
    /// <summary>The maximum ASCII outbox line length, excluding its console newline.</summary>
    public int KeyLoadOutboxMaximumBytes { get; set; } = DefaultKeyLoadOutboxMaximumBytes;
    /// <summary>The maximum outbox consumers inspected for a failure projection.</summary>
    public int KeyLoadOutboxMaximumConsumers { get; set; } = DefaultKeyLoadOutboxMaximumConsumers;

    /// <summary>Checks supported ceilings and the immutable fallback projection minima.</summary>
    /// <returns>Whether all diagnostic policy values are supported.</returns>
    public bool IsValid() => KurrentSetupMaximumCauses is >= MinimumPositiveLimit and <= DefaultKurrentSetupMaximumCauses
        && KurrentSetupMaximumFramesPerCause is >= MinimumPositiveLimit and <= DefaultKurrentSetupMaximumFramesPerCause
        && KurrentSetupMaximumIdentifierCharacters is >= MinimumPositiveLimit and <= DefaultKurrentSetupMaximumIdentifierCharacters
        && KurrentSetupBuilderCapacity is >= MinimumPositiveLimit and <= DefaultKurrentSetupBuilderCapacity
        && KurrentCleanupMaximumExceptionDepth is >= MinimumPositiveLimit and <= DefaultKurrentCleanupMaximumExceptionDepth
        && KurrentCleanupMaximumCharacters is >= MinimumPositiveLimit and <= DefaultKurrentCleanupMaximumCharacters
        && RedisReplicaMaximumCharacters >= RedisReplicaDiagnostics.Overflow.Length && RedisReplicaMaximumCharacters <= DefaultRedisReplicaMaximumCharacters
        && KeyLoadOutboxMaximumBytes >= KeyLoadOutboxDiagnosticLine.UnavailableLine.Length && KeyLoadOutboxMaximumBytes <= DefaultKeyLoadOutboxMaximumBytes
        && KeyLoadOutboxMaximumConsumers is >= MinimumPositiveLimit and <= DefaultKeyLoadOutboxMaximumConsumers;

    /// <summary>Rejects invalid configuration before diagnostic ownership.</summary>
    /// <returns>This validated native policy.</returns>
    public NativeComparisonDiagnosticOptions Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(NativeComparisonDiagnosticOptions), [ValidationMessage]);
        }
        return this;
    }

    /// <summary>Validates the real injected wrapper before native ownership.</summary>
    /// <param name="options">The centrally bound native diagnostic options.</param>
    /// <returns>The original validated wrapper.</returns>
    public static IOptions<NativeComparisonDiagnosticOptions> Require(IOptions<NativeComparisonDiagnosticOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        return options;
    }

    /// <summary>Records effective limits without any native exception or user text.</summary>
    /// <param name="parameters">The existing effective policy evidence dictionary.</param>
    public void RecordEvidence(IDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters[nameof(KurrentSetupMaximumCauses)] = KurrentSetupMaximumCauses.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(KurrentSetupMaximumFramesPerCause)] = KurrentSetupMaximumFramesPerCause.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(KurrentSetupMaximumIdentifierCharacters)] = KurrentSetupMaximumIdentifierCharacters.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(KurrentSetupBuilderCapacity)] = KurrentSetupBuilderCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(KurrentCleanupMaximumExceptionDepth)] = KurrentCleanupMaximumExceptionDepth.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(KurrentCleanupMaximumCharacters)] = KurrentCleanupMaximumCharacters.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(RedisReplicaMaximumCharacters)] = RedisReplicaMaximumCharacters.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(KeyLoadOutboxMaximumBytes)] = KeyLoadOutboxMaximumBytes.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(KeyLoadOutboxMaximumConsumers)] = KeyLoadOutboxMaximumConsumers.ToString(CultureInfo.InvariantCulture);
    }
}
