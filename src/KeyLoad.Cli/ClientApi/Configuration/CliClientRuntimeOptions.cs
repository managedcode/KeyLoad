using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.ClientApi;

internal sealed record CliClientRuntimeOptions(
    IOptions<CliConnectionOptions> Connection, IOptions<CliExecutionOptions> Execution,
    IOptions<KeyLoadClientExecutionOptions> ClientExecution);
