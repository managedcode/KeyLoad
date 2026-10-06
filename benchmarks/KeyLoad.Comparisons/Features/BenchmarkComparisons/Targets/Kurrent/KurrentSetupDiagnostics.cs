using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KurrentSetupDiagnostics(IOptions<NativeComparisonDiagnosticOptions> options)
{
    private readonly NativeComparisonDiagnosticOptions settings = NativeComparisonDiagnosticOptions.Require(options).Value;
    private const char TypeNameSeparator = '.';
    private const char NestedTypeSeparator = '+';
    private const char GenericAritySeparator = '`';
    private const char GenericOpenBracket = '<';
    private const char GenericCloseBracket = '>';
    private const char ArrayOpenBracket = '[';
    private const char ArrayCloseBracket = ']';
    private const char TypeArgumentSeparator = ',';

    private const char LowerHexadecimalStart = 'a';
    private const char IsIdentifierCharacterCharacter = 'z';
    private const char IsIdentifierCharacterCharacterToken = '0';
    private const char IsIdentifierCharacterIsIdentifierCharacterCharacterToken = '9';

    private const char UpperAsciiLetterStart = 'A';
    private const char UpperAsciiLetterEnd = 'Z';
    private const char IdentifierSeparator = '_';

    private const string UnknownIdentifier = "unknown";

    internal string Format(KurrentSetupStage stage, Exception failure)
    {
        const string KurrentSetupFailureStageToken = "KurrentSetupFailure|stage=";
        const int FirstElementIndex = 0;

        ArgumentNullException.ThrowIfNull(failure);
        var output = new StringBuilder(settings.KurrentSetupBuilderCapacity);
        output.Append(KurrentSetupFailureStageToken).Append(stage);
        var cause = failure;
        for (var causeIndex = FirstElementIndex; causeIndex < settings.KurrentSetupMaximumCauses && cause is not null; causeIndex++)
        {
            AppendCause(output, cause, causeIndex);
            cause = cause.InnerException;
        }
        return output.ToString();
    }

    internal void TryWrite(KurrentSetupStage stage, Exception failure)
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

    internal string SafeIdentifier(string? identifier)
    {
        const int FirstElementIndex = 0;
        const char IdentifierSeparator = '_';

        if (string.IsNullOrEmpty(identifier))
        {
            return UnknownIdentifier[..Math.Min(UnknownIdentifier.Length, settings.KurrentSetupMaximumIdentifierCharacters)];
        }

        var output = new StringBuilder(Math.Min(identifier.Length, settings.KurrentSetupMaximumIdentifierCharacters));
        for (var index = FirstElementIndex; index < identifier.Length && output.Length < settings.KurrentSetupMaximumIdentifierCharacters; index++)
        {
            var character = identifier[index];
            output.Append(IsIdentifierCharacter(character) ? character : IdentifierSeparator);
        }
        return output.ToString();
    }

    private void AppendCause(StringBuilder output, Exception cause, int causeIndex)
    {
        const string CauseToken = "|cause[";
        const string IndexedDiagnosticAssignment = "]=";
        const int FirstElementIndex = 0;

        var typeName = cause.GetType().FullName;
        output.Append(CauseToken).Append(causeIndex).Append(IndexedDiagnosticAssignment)
            .Append(SafeIdentifier(typeName));
        var trace = new StackTrace(cause, false);
        for (var frameIndex = FirstElementIndex; frameIndex < settings.KurrentSetupMaximumFramesPerCause; frameIndex++)
        {
            var frame = trace.GetFrame(frameIndex);
            if (frame is null)
            {
                break;
            }
            AppendFrame(output, frame, frameIndex);
        }
    }

    private void AppendFrame(StringBuilder output, StackFrame frame, int frameIndex)
    {
        const string FrameToken = "|frame[";
        const string IndexedDiagnosticAssignment = "]=";
        const char VersionSeparator = '.';

        var method = frame.GetMethod();
        output.Append(FrameToken).Append(frameIndex).Append(IndexedDiagnosticAssignment)
            .Append(SafeIdentifier(method?.DeclaringType?.FullName))
            .Append(VersionSeparator)
            .Append(SafeIdentifier(method?.Name));
    }

    private static bool IsIdentifierCharacter(char character)
        => character is >= UpperAsciiLetterStart and <= UpperAsciiLetterEnd or >= LowerHexadecimalStart and <= IsIdentifierCharacterCharacter or >= IsIdentifierCharacterCharacterToken and <= IsIdentifierCharacterIsIdentifierCharacterCharacterToken
            or TypeNameSeparator or IdentifierSeparator or NestedTypeSeparator or GenericAritySeparator or GenericOpenBracket or GenericCloseBracket or ArrayOpenBracket or ArrayCloseBracket or TypeArgumentSeparator;
}
