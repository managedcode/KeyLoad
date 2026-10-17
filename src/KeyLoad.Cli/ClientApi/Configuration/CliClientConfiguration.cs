using System.Globalization;
using System.Text.Json;
using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.ClientApi;

/// <summary>Resolves the standalone command's sources into validated native options.</summary>
[ConfigurationBinding]
internal static class CliClientConfiguration
{
    private const string AdminKeyPropertyName = "AdminKey";
    private const string KeyEnvironmentVariableName = "KEYLOAD_API_KEY";
    private const string StatusTimeoutEnvironmentVariableName = "KEYLOAD_CLI_STATUS_TIMEOUT";
    private const string ProblemBodyEnvironmentVariableName = "KEYLOAD_CLIENT__MAXPROBLEMBODYBYTES";
    private const string ConnectionValidationMessage = "The CLI endpoint and credential must be present.";
    private const int EndpointArgument = 1;
    private const int ProfileArgument = 2;
    private const int ArgumentsWithProfile = 3;

    internal static CliClientRuntimeOptions Read(string[] args)
    {
        var connection = Create<CliConnectionOptions>(options =>
        {
            options.ApiKey = Environment.GetEnvironmentVariable(KeyEnvironmentVariableName)
                ?? (args.Length == ArgumentsWithProfile ? ProfileKey(args[ProfileArgument]) : null)
                ?? throw Errors.Fail(ErrorCode.Unauthenticated,
                    CliClientApi.GetMessage(CliClientApi.MissingCredentialMessageKey));
            options.Endpoint = new Uri(args[EndpointArgument]);
        }, options => options.IsValid(), ConnectionValidationMessage);
        var execution = Create<CliExecutionOptions>(options =>
        {
            var configured = Environment.GetEnvironmentVariable(StatusTimeoutEnvironmentVariableName);
            if (configured is not null)
            {
                if (!TimeSpan.TryParse(configured, CultureInfo.InvariantCulture, out var timeout))
                {
                    throw new OptionsValidationException(Options.DefaultName, typeof(CliExecutionOptions),
                        [CliExecutionOptions.ValidationMessage]);
                }
                options.StatusTimeout = timeout;
            }
        }, options => options.IsValid(), CliExecutionOptions.ValidationMessage);
        var client = ReadClient();
        _ = connection.Value;
        _ = execution.Value;
        _ = client.Value;
        return new(connection, execution, client);
    }

    private static OptionsManager<KeyLoadClientExecutionOptions> ReadClient()
    {
        return Create<KeyLoadClientExecutionOptions>(options =>
        {
            var configured = Environment.GetEnvironmentVariable(ProblemBodyEnvironmentVariableName);
            if (configured is not null)
            {
                if (!int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximumBytes))
                {
                    throw new OptionsValidationException(Options.DefaultName, typeof(KeyLoadClientExecutionOptions),
                        [KeyLoadClientExecutionOptions.ValidationMessage]);
                }
                options.MaximumProblemBodyBytes = maximumBytes;
            }
        }, options => options.IsValid(), KeyLoadClientExecutionOptions.ValidationMessage);
    }

    private static OptionsManager<T> Create<T>(Action<T> configure, Func<T, bool> validate, string message)
        where T : class, new()
        => new(new OptionsFactory<T>(
            [new ConfigureNamedOptions<T>(Options.DefaultName, configure)], [],
            [new ValidateOptions<T>(Options.DefaultName, validate, message)]));

    private static string ProfileKey(string path)
    {
        using var profile = JsonDocument.Parse(File.ReadAllBytes(path));
        return profile.RootElement.GetProperty(AdminKeyPropertyName).GetString()!;
    }
}

internal sealed record CliClientRuntimeOptions(
    IOptions<CliConnectionOptions> Connection, IOptions<CliExecutionOptions> Execution,
    IOptions<KeyLoadClientExecutionOptions> ClientExecution);
