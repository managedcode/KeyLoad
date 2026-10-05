using System.Globalization;
using System.Resources;

namespace KeyLoad.AppHost.Features.ClusterReplication.Commands;

/// <summary>Runs the explicitly requested, offline-only V1 profile conversion.</summary>
internal static class ClusterProfileUpgradeCommand
{
    internal const string UpgradeFlag = "--keyload-profile-upgrade-v1-to-v2";
    internal const string DataRootFlag = "--data-root";
    private const string UpgradePrefix = "--keyload-profile-upgrade";
    private const int InvalidArgumentsExitCode = 2;
    private const int ConversionFailedExitCode = 1;
    private const int MaximumPathLength = 4096;
    private const string MessagesBaseName = "KeyLoad.AppHost.Features.ClusterReplication.Commands.ClusterProfileUpgradeMessages";
    private const string InvalidArgumentsMessage = "InvalidArguments";
    private const string ConversionFailedMessage = "ConversionFailed";
    private const string SuccessMessage = "Success";
    private static readonly ResourceManager Messages = new(MessagesBaseName, typeof(ClusterProfileUpgradeCommand).Assembly);

    /// <summary>Returns null for ordinary AppHost invocations; a matched or malformed reserved command is handled here.</summary>
    internal static int? Dispatch(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!ContainsReservedArgument(args))
        { return null; }
        if (!HasExactArguments(args))
        {
            Console.Error.WriteLine(GetMessage(InvalidArgumentsMessage));
            return InvalidArgumentsExitCode;
        }

        try
        {
            _ = ClusterProfileStore.UpgradeLegacyOffline(args[2]);
            Console.WriteLine(GetMessage(SuccessMessage));
            return 0;
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

    private static bool HasExactArguments(string[] args)
        => args.Length == 3 && args[0] == UpgradeFlag && args[1] == DataRootFlag
            && args[2].Length <= MaximumPathLength && !args[2].Contains('\0', StringComparison.Ordinal)
            && Path.IsPathFullyQualified(args[2]) && Directory.Exists(args[2]);

    private static bool IsExpectedFailure(Exception exception)
        => exception is ArgumentException or IOException or InvalidOperationException
            or UnauthorizedAccessException or NotSupportedException;

    private static string GetMessage(string key)
        => Messages.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException("The offline profile command message is missing.");
}
