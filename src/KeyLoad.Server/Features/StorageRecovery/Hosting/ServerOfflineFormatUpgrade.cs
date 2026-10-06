using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server;

/// <summary>Dispatches an explicit local offline upgrade before any server or replica starts.</summary>
internal static class ServerOfflineFormatUpgrade
{
    private const string Command = "upgrade-native-store";
    private const string PrepareNode = "prepare-native-node";
    private const string VerifyNode = "verify-native-node";
    private const string PublishNode = "publish-native-node";
    private const string SourceOption = "--source=";
    private const string DestinationOption = "--destination=";
    private const string Usage = "Use upgrade-native-store, prepare-native-node, verify-native-node or publish-native-node with --source=<stopped-source> --destination=<new-target>.";
    private const string Complete = "Native store upgrade completed. Verify every RF3 store before starting compatible voters.";
    private const int ArgumentCount = 3;
    private const int MaximumPathCharacters = 4096;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        const int EmptyArgsLength = 0;
        const int ArgsFirstIndex = 0;
        const int ExitCodeInitialValue = 1;

        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == EmptyArgsLength || args[ArgsFirstIndex] is not (Command or PrepareNode or VerifyNode or PublishNode))
        {
            return false;
        }

        try
        {
            var paths = Parse(args);
            await ExecuteAsync(args[ArgsFirstIndex], paths.Source, paths.Destination).ConfigureAwait(false);
        }
        catch (KeyLoadException failure)
        {
            await Console.Error.WriteLineAsync(failure.Code.ToString()).ConfigureAwait(false);
            Environment.ExitCode = ExitCodeInitialValue;
        }
        return true;
    }

    private static async Task ExecuteAsync(string operation, string source, string destination)
    {
        const string ExecuteAsyncValueText = "Node prepared. Prepare and verify every stopped RF3 node before publishing any node.";
        const string ExecuteAsyncExecuteAsyncValueText = "Node verified. Verify all three current nodes before starting compatible voters.";

        if (operation == Command)
        {
            _ = ZoneTreeFormatUpgrade.Upgrade(source, new(destination), ServerConfiguration.ReadOfflineStorageExecution());
            await Console.Out.WriteLineAsync(Complete).ConfigureAwait(false);
            return;
        }
        var options = ServerConfiguration.ReadOfflineRuntimeOptions(destination);
        _ = operation switch
        {
            PrepareNode => ServerNodeFormatUpgrade.Prepare(source, options),
            VerifyNode => ServerNodeFormatUpgrade.VerifyPrepared(source, options),
            _ => ServerNodeFormatUpgrade.Publish(source, options)
        };
        await Console.Out.WriteLineAsync(operation switch
        {
            PrepareNode => ExecuteAsyncValueText,
            VerifyNode => ExecuteAsyncExecuteAsyncValueText,
            _ => "Node published. Verify all three current nodes before starting compatible voters."
        }).ConfigureAwait(false);
    }

    private static (string Source, string Destination) Parse(string[] args)
    {
        const int StartSingleItemCount = 1;

        if (args.Length != ArgumentCount)
        {
            throw Errors.Fail(ErrorCode.Validation, Usage);
        }

        string? source = null;
        string? destination = null;
        foreach (var argument in args.AsSpan(StartSingleItemCount))
        {
            if (argument.StartsWith(SourceOption, StringComparison.Ordinal) && source is null)
            {
                source = PathArgument(argument, SourceOption);
            }
            else if (argument.StartsWith(DestinationOption, StringComparison.Ordinal) && destination is null)
            {
                destination = PathArgument(argument, DestinationOption);
            }
            else
            {
                throw Errors.Fail(ErrorCode.Validation, Usage);
            }
        }
        return (source ?? throw Errors.Fail(ErrorCode.Validation, Usage),
            destination ?? throw Errors.Fail(ErrorCode.Validation, Usage));
    }

    private static string PathArgument(string argument, string prefix)
    {
        const char NullCharacter = '\0';

        var value = argument[prefix.Length..];
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumPathCharacters || value.Contains(NullCharacter, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, Usage);
        }
        return value;
    }
}
