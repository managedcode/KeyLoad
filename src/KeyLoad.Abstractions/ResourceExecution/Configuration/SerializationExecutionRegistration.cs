using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad;

/// <summary>Owns the single validated native options wrapper for process-static serializers.</summary>
[ConfigurationBinding]
public static class SerializationExecutionRegistration
{
    private const string EnvironmentPrefix = "KEYLOAD_SERIALIZATION__";
    private static readonly Lazy<IOptions<SerializationExecutionOptions>> Captured = new(ReadProcess);

    /// <summary>Gets the same validated wrapper for startup preflight and serializer execution.</summary>
    public static IOptions<SerializationExecutionOptions> Process => Captured.Value;

    private static IOptions<SerializationExecutionOptions> ReadProcess()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables(EnvironmentPrefix).Build();
        using var lifetime = configuration as IDisposable;
        return Bind(configuration);
    }

    internal static IOptions<SerializationExecutionOptions> Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var factory = new OptionsFactory<SerializationExecutionOptions>(
            [new ConfigureFromConfigurationOptions<SerializationExecutionOptions>(configuration)], [],
            [new ValidateOptions<SerializationExecutionOptions>(Options.DefaultName,
                static options => options.IsValid(), SerializationExecutionOptions.ValidationMessage)]);
        var options = new OptionsManager<SerializationExecutionOptions>(factory);
        _ = options.Value;
        return options;
    }
}
