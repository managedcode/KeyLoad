using System.Text.Json;
using KeyLoad;
using KeyLoad.Cli.Features.ClientApi;

const int NoArguments = 0;
const int InvalidCommandExitCode = 2;
const int FailedOperationExitCode = 1;

try
{
    if (args.Length == NoArguments)
    {
        CliClientApi.Help();
        return;
    }

    if (!CliCommandDispatcher.TryGetCommand(args, out var command))
    {
        CliClientApi.Help();
        Environment.ExitCode = InvalidCommandExitCode;
        return;
    }

    await CliCommandDispatcher.DispatchAsync(command, args);
}
catch (KeyLoadException exception)
{
    var problem = JsonSerializer.Serialize(exception.ToProblem(), JsonDefaults.Options);
    await Console.Error.WriteLineAsync(problem);
    Environment.ExitCode = FailedOperationExitCode;
}
