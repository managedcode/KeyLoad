using System.Globalization;
using System.Resources;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.ClusterReplication.Commands;

/// <summary>Runs the explicitly requested, offline-only V1 profile conversion.</summary>
[KeyLoad.ConfigurationBinding]
internal static class ClusterProfileUpgradeCommand
{
    private const int ArgumentCount = 3;
    private const int HasExactArgumentsFirstIndex = 0;
    private const int HasExactArgumentsSecondIndex = 1;
    private const int HasExactArgumentsElementIndex = 2;
    private const char HasExactArgumentsNullCharacter = '\0';
    private const string GetMessageMessageText = "The offline profile command message is missing.";

    internal const string UpgradeFlag = "--keyload-profile-upgrade-v1-to-v2";
    internal const string DataRootFlag = "--data-root";
    private const string UpgradePrefix = "--keyload-profile-upgrade";
    private const int InvalidArgumentsExitCode = 2;
    private const int ConversionFailedExitCode = 1;
    private const string MessagesBaseName = "KeyLoad.AppHost.Features.ClusterReplication.Commands.ClusterProfileUpgradeMessages";
    private const string InvalidArgumentsMessage = "InvalidArguments";
    private const string ConversionFailedMessage = "ConversionFailed";
    private const string SuccessMessage = "Success";
    private static readonly ResourceManager Messages = new(MessagesBaseName, typeof(ClusterProfileUpgradeCommand).Assembly);

    /// <summary>Returns null for ordinary AppHost invocations; a matched or malformed reserved command is handled here.</summary>
    internal static int? Dispatch(string[] args)
    {
        const int ElementIndex = 2;
        const int EmptyResult = 0;

        ArgumentNullException.ThrowIfNull(args);
        if (!ContainsReservedArgument(args))
        { return null; }
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var executionOptions = AppHostOptionsRegistration.BindProfileExecution(configuration);
        if (!HasExactArguments(args, executionOptions))
        {
            Console.Error.WriteLine(GetMessage(InvalidArgumentsMessage));
            return InvalidArgumentsExitCode;
        }

        try
        {
            _ = ClusterProfileStore.UpgradeLegacyOffline(args[ElementIndex], executionOptions);
            Console.WriteLine(GetMessage(SuccessMessage));
            return EmptyResult;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Console.Error.WriteLine(GetMessage(ConversionFailedMessage));
            return ConversionFailedExitCode;
        }
    }

    private static bool ContainsReservedArgument(string[] args)
        => args.Any(argument => argument.StartsWith(UpgradePrefix, StringComparison.Ordinal)
            || argument.StartsWith(DataRootFlag, StringComparison.Ordinal));

    private static bool HasExactArguments(string[] args, IOptions<ClusterProfileExecutionOptions> executionOptions)
        => args.Length == ArgumentCount && args[HasExactArgumentsFirstIndex] == UpgradeFlag && args[HasExactArgumentsSecondIndex] == DataRootFlag
            && args[HasExactArgumentsElementIndex].Length <= executionOptions.Value.MaximumPathCharacters && !args[HasExactArgumentsElementIndex].Contains(HasExactArgumentsNullCharacter, StringComparison.Ordinal)
            && Path.IsPathFullyQualified(args[HasExactArgumentsElementIndex]) && Directory.Exists(args[HasExactArgumentsElementIndex]);

    private static bool IsExpectedFailure(Exception exception)
        => exception is ArgumentException or IOException or InvalidOperationException
            or UnauthorizedAccessException or NotSupportedException;

    private static string GetMessage(string key)
        => Messages.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException(GetMessageMessageText);
}
