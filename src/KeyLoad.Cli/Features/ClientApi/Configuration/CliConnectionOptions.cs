namespace KeyLoad.Cli.Features.ClientApi;

/// <summary>The command's centrally resolved endpoint and credential.</summary>
[ConfigurationOptions]
internal sealed class CliConnectionOptions
{
    internal Uri Endpoint { get; set; } = null!;
    internal string ApiKey { get; set; } = null!;

    internal bool IsValid() => Endpoint is not null && ApiKey is not null;
}
