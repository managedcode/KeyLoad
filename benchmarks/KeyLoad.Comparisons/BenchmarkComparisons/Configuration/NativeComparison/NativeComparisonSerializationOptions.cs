using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Initial native SQL builder reservations, independent of vector and output bounds.</summary>
[ConfigurationOptions]
public sealed class NativeComparisonSerializationOptions
{
    private const int MinimumPositiveCapacity = 1;
    private const int DefaultSurrealDbBatchRecordBuilderCapacity = 12_000;
    private const int DefaultSurrealDbVectorComponentBuilderCapacity = 12;
    private const int DefaultPostgresVectorComponentBuilderCapacity = 14;
    /// <summary>The centrally bound native serialization policy section.</summary>
    public const string SectionName = "NativeComparisonSerialization";
    /// <summary>The failure category for an unsupported builder reservation.</summary>
    public const string ValidationMessage = "Native comparison serialization reservations must be present, positive and within their supported bounds.";
    /// <summary>The initial SurrealDB SQL character reservation per batch record.</summary>
    public int SurrealDbBatchRecordBuilderCapacity { get; set; } = DefaultSurrealDbBatchRecordBuilderCapacity;
    /// <summary>The initial SurrealDB vector character reservation per component.</summary>
    public int SurrealDbVectorComponentBuilderCapacity { get; set; } = DefaultSurrealDbVectorComponentBuilderCapacity;
    /// <summary>The initial PostgreSQL vector character reservation per component.</summary>
    public int PostgresVectorComponentBuilderCapacity { get; set; } = DefaultPostgresVectorComponentBuilderCapacity;

    /// <summary>Checks the original inclusive reservation ceilings.</summary>
    /// <returns>Whether every configured reservation is supported.</returns>
    public bool IsValid() => SurrealDbBatchRecordBuilderCapacity is >= MinimumPositiveCapacity and <= DefaultSurrealDbBatchRecordBuilderCapacity
        && SurrealDbVectorComponentBuilderCapacity is >= MinimumPositiveCapacity and <= DefaultSurrealDbVectorComponentBuilderCapacity
        && PostgresVectorComponentBuilderCapacity is >= MinimumPositiveCapacity and <= DefaultPostgresVectorComponentBuilderCapacity;

    /// <summary>Rejects invalid policy before native target ownership.</summary>
    /// <returns>This validated policy.</returns>
    public NativeComparisonSerializationOptions Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(NativeComparisonSerializationOptions), [ValidationMessage]);
        }
        return this;
    }

    /// <summary>Validates the original required native options wrapper.</summary>
    /// <param name="options">The centrally bound serialization policy.</param>
    /// <returns>The original validated wrapper.</returns>
    public static IOptions<NativeComparisonSerializationOptions> Require(IOptions<NativeComparisonSerializationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        return options;
    }

    /// <summary>Records the actual reservations in the existing native parameter dictionary.</summary>
    /// <param name="parameters">The existing receipt evidence dictionary.</param>
    public void RecordEvidence(IDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters[nameof(SurrealDbBatchRecordBuilderCapacity)] = SurrealDbBatchRecordBuilderCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(SurrealDbVectorComponentBuilderCapacity)] = SurrealDbVectorComponentBuilderCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(PostgresVectorComponentBuilderCapacity)] = PostgresVectorComponentBuilderCapacity.ToString(CultureInfo.InvariantCulture);
    }
}
