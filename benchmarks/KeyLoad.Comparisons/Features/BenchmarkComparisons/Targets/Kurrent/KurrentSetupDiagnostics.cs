using System.Diagnostics;
using System.Text;

namespace KeyLoad.Comparisons.Targets;

internal static class KurrentSetupDiagnostics
{
    private const int MaximumCauses = 3;
    private const int MaximumFramesPerCause = 8;
    private const int MaximumIdentifierCharacters = 64;
    private const int MaximumLineBytes = 4_096;
    private const string UnknownIdentifier = "unknown";

    internal static string Format(KurrentSetupStage stage, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var output = new StringBuilder(MaximumLineBytes);
        output.Append("KurrentSetupFailure|stage=").Append(stage);
        var cause = failure;
        for (var causeIndex = 0; causeIndex < MaximumCauses && cause is not null; causeIndex++)
        {
            AppendCause(output, cause, causeIndex);
            cause = cause.InnerException;
        }
        return output.ToString();
    }

    internal static void TryWrite(KurrentSetupStage stage, Exception failure)
    {
        try
        {
            Console.Error.WriteLine(Format(stage, failure));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or NotSupportedException or TypeLoadException or MemberAccessException)
        {
            // Recoverable diagnostic output/metadata failures preserve the caught setup exception.
        }
    }

    internal static string SafeIdentifier(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return UnknownIdentifier;
        }

        var output = new StringBuilder(Math.Min(identifier.Length, MaximumIdentifierCharacters));
        for (var index = 0; index < identifier.Length && output.Length < MaximumIdentifierCharacters; index++)
        {
            var character = identifier[index];
            output.Append(IsIdentifierCharacter(character) ? character : '_');
        }
        return output.ToString();
    }

    private static void AppendCause(StringBuilder output, Exception cause, int causeIndex)
    {
        var typeName = cause.GetType().FullName;
        output.Append("|cause[").Append(causeIndex).Append("]=")
            .Append(SafeIdentifier(typeName));
        var trace = new StackTrace(cause, false);
        for (var frameIndex = 0; frameIndex < MaximumFramesPerCause; frameIndex++)
        {
            var frame = trace.GetFrame(frameIndex);
            if (frame is null)
            {
                break;
            }
            AppendFrame(output, frame, frameIndex);
        }
    }

    private static void AppendFrame(StringBuilder output, StackFrame frame, int frameIndex)
    {
        var method = frame.GetMethod();
        output.Append("|frame[").Append(frameIndex).Append("]=")
            .Append(SafeIdentifier(method?.DeclaringType?.FullName))
            .Append('.')
            .Append(SafeIdentifier(method?.Name));
    }

    private static bool IsIdentifierCharacter(char character)
        => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '.' or '_' or '+' or '`' or '<' or '>' or '[' or ']' or ',';
}
