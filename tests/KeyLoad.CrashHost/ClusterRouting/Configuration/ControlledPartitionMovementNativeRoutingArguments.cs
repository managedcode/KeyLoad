using System.Globalization;
using System.Collections.Immutable;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Threads the selected validated routing profile into the existing real Server configuration.</summary>
[ConfigurationBinding]
internal static class ControlledPartitionMovementNativeRoutingArguments
{
    private const string NativeSiloInformationArgument = "--Logging:LogLevel:Orleans.Runtime.Silo=Information";
    private const string ArgumentPrefix = "--";
    private const string Separator = ":";
    private const string Assignment = "=";

    internal static ImmutableArray<string> Capture(IOptions<GrainRoutingOptions>? options)
    {
        if (options is null)
        {
            return [];
        }
        var arguments = new List<string>();
        var value = options.Value;
        value.Validate();
        arguments.Add(NativeSiloInformationArgument);
        Add(arguments, nameof(GrainRoutingOptions.RequestLifetime), value.RequestLifetime);
        Add(arguments, nameof(GrainRoutingOptions.MaximumFuture), value.MaximumFuture);
        Add(arguments, nameof(GrainRoutingOptions.ExecutionLifetime), value.ExecutionLifetime);
        Add(arguments, nameof(GrainRoutingOptions.MaximumRequestProducers), value.MaximumRequestProducers);
        Add(arguments, nameof(GrainRoutingOptions.MaximumTotalFrames), value.MaximumTotalFrames);
        Add(arguments, nameof(GrainRoutingOptions.MaximumConnections), value.MaximumConnections);
        Add(arguments, nameof(GrainRoutingOptions.MaximumConnectionOperations), value.MaximumConnectionOperations);
        Add(arguments, nameof(GrainRoutingOptions.ConnectionIdleTimeout), value.ConnectionIdleTimeout);
        Add(arguments, nameof(GrainRoutingOptions.MaximumReplyBytes), value.MaximumReplyBytes);
        Add(arguments, nameof(GrainRoutingOptions.InitialReplyBufferBytes), value.InitialReplyBufferBytes);
        Add(arguments, nameof(GrainRoutingOptions.MaximumStartedBytes), value.MaximumStartedBytes);
        Add(arguments, nameof(GrainRoutingOptions.MaximumFailedBytes), value.MaximumFailedBytes);
        Add(arguments, nameof(GrainRoutingOptions.MaximumCompletedBytes), value.MaximumCompletedBytes);
        Add(arguments, nameof(GrainRoutingOptions.MaximumAggregateBytes), value.MaximumAggregateBytes);
        Add(arguments, nameof(GrainRoutingOptions.MaximumScratchBytes), value.MaximumScratchBytes);
        Add(arguments, nameof(GrainRoutingOptions.MinimumScratchBytes), value.MinimumScratchBytes);
        Add(arguments, nameof(GrainRoutingOptions.MaximumDetailCharacters), value.MaximumDetailCharacters);
        Add(arguments, nameof(GrainRoutingOptions.MaximumPrincipalBytes), value.MaximumPrincipalBytes);
        Add(arguments, nameof(GrainRoutingOptions.MaximumContextBytes), value.MaximumContextBytes);
        return [.. arguments];
    }

    private const int NoRoutingArguments = 0;
    private const int SingleCategoryArgument = 1;
    private static readonly ImmutableArray<string> Properties =
    [
        nameof(GrainRoutingOptions.RequestLifetime),
        nameof(GrainRoutingOptions.MaximumFuture),
        nameof(GrainRoutingOptions.ExecutionLifetime),
        nameof(GrainRoutingOptions.MaximumRequestProducers),
        nameof(GrainRoutingOptions.MaximumTotalFrames),
        nameof(GrainRoutingOptions.MaximumConnections),
        nameof(GrainRoutingOptions.MaximumConnectionOperations),
        nameof(GrainRoutingOptions.ConnectionIdleTimeout),
        nameof(GrainRoutingOptions.MaximumReplyBytes),
        nameof(GrainRoutingOptions.InitialReplyBufferBytes),
        nameof(GrainRoutingOptions.MaximumStartedBytes),
        nameof(GrainRoutingOptions.MaximumFailedBytes),
        nameof(GrainRoutingOptions.MaximumCompletedBytes),
        nameof(GrainRoutingOptions.MaximumAggregateBytes),
        nameof(GrainRoutingOptions.MaximumScratchBytes),
        nameof(GrainRoutingOptions.MinimumScratchBytes),
        nameof(GrainRoutingOptions.MaximumDetailCharacters),
        nameof(GrainRoutingOptions.MaximumPrincipalBytes),
        nameof(GrainRoutingOptions.MaximumContextBytes),
    ];

    internal static ImmutableArray<string> Restore(ImmutableArray<string> original)
    {
        var prefix = ArgumentPrefix + GrainRoutingOptions.SectionName + Separator;
        var selected = original.Where(argument => argument.StartsWith(prefix, StringComparison.Ordinal)
            || argument == NativeSiloInformationArgument).ToImmutableArray();
        if (selected.Length == NoRoutingArguments)
        {
            return [];
        }
        if (selected.Length != Properties.Length + SingleCategoryArgument
            || selected.Count(argument => argument == NativeSiloInformationArgument) != SingleCategoryArgument
            || Properties.Any(property => selected.Count(argument => argument.StartsWith(
                prefix + property + Assignment, StringComparison.Ordinal)) != SingleCategoryArgument))
        {
            throw ControlledPartitionMovementNativeHostInputValidation.Invalid();
        }
        return selected;
    }

    private static void Add<T>(List<string> arguments, string property, T value)
        => arguments.Add(ArgumentPrefix + GrainRoutingOptions.SectionName + Separator + property
            + Assignment + Convert.ToString(value, CultureInfo.InvariantCulture));
}
