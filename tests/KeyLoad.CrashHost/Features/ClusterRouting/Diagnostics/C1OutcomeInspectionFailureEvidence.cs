using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionFailureEvidence
{
    private const int MaximumAggregateDepth = 16;
    private const int EmptyCount = 0;
    private const int MinimumFields = 4;
    private const int MaximumFields = 5;
    private const int PrefixIndex = 0;
    private const int VersionIndex = 1;
    private const int PhaseIndex = 2;
    private const int KindIndex = 3;
    private const int CodeIndex = 4;
    private const int NativeOperationDenied = 1;
    private const int NativeMissingPath = 2;
    private const int NativeIoFailure = 5;
    private const int NativeBadFileDescriptor = 9;
    private const int NativeTryAgain = 11;
    private const int NativeAccessDenied = 13;
    private const int NativeResourceBusy = 16;
    private const int NativePathIsNotDirectory = 20;
    private const int NativeInvalidArgument = 22;
    private const int NativeOpenFileLimit = 24;
    private const int NativeNoSpace = 28;
    private const int NativeBlockingConflict = 35;
    private const int NativeLinkLoop = 40;
    private const string PrefixField = "C1_OUTCOME_FAILURE";
    private const string VersionField = "v=1";
    private const string PhaseField = "phase=";
    private const string KindField = "kind=";
    private const string CodeField = "code=";
    private const string FieldSeparator = " ";
    private const string LineFeed = "\n";
    private const string Prefix = PrefixField + FieldSeparator + VersionField;
    private const string InvalidEvidence = "The original outcome inspection failure evidence is invalid.";
    internal const int MaximumBytes = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private C1OutcomeInspectionFailurePhase phase = C1OutcomeInspectionFailurePhase.ReadInput;
    internal C1OutcomeInspectionFailureRecord? FirstFailure { get; private set; }

    internal void SetPhase(C1OutcomeInspectionFailurePhase value) => phase = value;

    internal void Capture(Exception error)
    {
        if (FirstFailure is not null)
        { return; }
        var cause = FirstCause(error);
        FirstFailure = new(phase, Kind(cause), SafeCode(cause));
    }

    internal byte[] Bytes()
        => Encode(FirstFailure ?? throw new InvalidOperationException(InvalidEvidence));

    internal static C1OutcomeInspectionFailureRecord Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is <= EmptyCount or > MaximumBytes)
        { throw new InvalidDataException(InvalidEvidence); }
        var fields = ReadText(bytes).Split(FieldSeparator, StringSplitOptions.None);
        if (fields.Length is < MinimumFields or > MaximumFields || fields[PrefixIndex] != PrefixField || fields[VersionIndex] != VersionField
            || !ReadPhase(fields[PhaseIndex], out var failurePhase) || !ReadKind(fields[KindIndex], out var failureKind))
        { throw new InvalidDataException(InvalidEvidence); }
        int? code = null;
        if (fields.Length == MaximumFields)
        {
            if (!fields[CodeIndex].StartsWith(CodeField, StringComparison.Ordinal)
                || !int.TryParse(fields[CodeIndex].AsSpan(CodeField.Length).TrimEnd(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsed) || !AllowedCode(failureKind, parsed))
            { throw new InvalidDataException(InvalidEvidence); }
            code = parsed;
        }
        var result = new C1OutcomeInspectionFailureRecord(failurePhase, failureKind, code);
        if (!bytes.SequenceEqual(Encode(result)))
        { throw new InvalidDataException(InvalidEvidence); }
        return result;
    }

    internal static string Describe(C1OutcomeInspectionFailureRecord record)
        => StrictUtf8.GetString(Encode(record)).TrimEnd();

    private static byte[] Encode(C1OutcomeInspectionFailureRecord record)
    {
        var code = record.Code is { } value ? FieldSeparator + CodeField + value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return StrictUtf8.GetBytes($"{Prefix}{FieldSeparator}{PhaseField}{record.Phase}{FieldSeparator}{KindField}{record.Kind}{code}{LineFeed}");
    }

    private static string ReadText(ReadOnlySpan<byte> bytes)
    {
        try
        { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException)
        { throw new InvalidDataException(InvalidEvidence); }
    }

    private static bool ReadPhase(string field, out C1OutcomeInspectionFailurePhase value)
    {
        value = default;
        return field.StartsWith(PhaseField, StringComparison.Ordinal)
            && Enum.TryParse(field[PhaseField.Length..].TrimEnd(), out value) && Enum.IsDefined(value);
    }

    private static bool ReadKind(string field, out C1OutcomeInspectionFailureKind value)
    {
        value = default;
        return field.StartsWith(KindField, StringComparison.Ordinal)
            && Enum.TryParse(field[KindField.Length..].TrimEnd(), out value) && Enum.IsDefined(value);
    }

    private static Exception FirstCause(Exception error)
    {
        for (var depth = EmptyCount; depth < MaximumAggregateDepth; depth++)
        {
            var next = error switch
            {
                AggregateException aggregate when aggregate.InnerExceptions.Count > EmptyCount
                    => aggregate.InnerExceptions[PrefixIndex],
                TypeInitializationException initialization => initialization.InnerException,
                System.Reflection.TargetInvocationException invocation => invocation.InnerException,
                _ => null
            };
            if (next is null)
            { return error; }
            error = next;
        }
        return error;
    }

    private static C1OutcomeInspectionFailureKind Kind(Exception error) => error switch
    {
        KeyLoadException => C1OutcomeInspectionFailureKind.KeyLoad,
        global::ZoneTree.Exceptions.WriteAheadLogCorruptionException => C1OutcomeInspectionFailureKind.WalCorruption,
        global::ZoneTree.Exceptions.WriteAheadLogFullLogCorruptionException => C1OutcomeInspectionFailureKind.WalFullLogCorruption,
        InvalidDataException => C1OutcomeInspectionFailureKind.InvalidData,
        FileNotFoundException => C1OutcomeInspectionFailureKind.MissingFile,
        DirectoryNotFoundException => C1OutcomeInspectionFailureKind.MissingDirectory,
        UnauthorizedAccessException => C1OutcomeInspectionFailureKind.Unauthorized,
        System.IO.FileLoadException => C1OutcomeInspectionFailureKind.AssemblyLoad,
        IOException => C1OutcomeInspectionFailureKind.Io,
        ArgumentException => C1OutcomeInspectionFailureKind.Argument,
        InvalidOperationException => C1OutcomeInspectionFailureKind.InvalidOperation,
        JsonException => C1OutcomeInspectionFailureKind.Json,
        global::ZoneTree.Exceptions.DatabaseNotFoundException => C1OutcomeInspectionFailureKind.NativeDatabaseMissing,
        global::Orleans.Serialization.SerializerException => C1OutcomeInspectionFailureKind.Serialization,
        TypeInitializationException => C1OutcomeInspectionFailureKind.TypeInitialization,
        System.Reflection.TargetInvocationException => C1OutcomeInspectionFailureKind.Invocation,
        KeyNotFoundException => C1OutcomeInspectionFailureKind.MissingKey,
        InvalidCastException => C1OutcomeInspectionFailureKind.InvalidCast,
        NotSupportedException => C1OutcomeInspectionFailureKind.NotSupported,
        OperationCanceledException => C1OutcomeInspectionFailureKind.Cancelled,
        NullReferenceException => C1OutcomeInspectionFailureKind.NullReference,
        IndexOutOfRangeException => C1OutcomeInspectionFailureKind.Range,
        OverflowException => C1OutcomeInspectionFailureKind.Overflow,
        FormatException => C1OutcomeInspectionFailureKind.Format,
        TimeoutException => C1OutcomeInspectionFailureKind.Timeout,
        DllNotFoundException => C1OutcomeInspectionFailureKind.NativeLibraryMissing,
        EntryPointNotFoundException => C1OutcomeInspectionFailureKind.NativeEntryPointMissing,
        TypeLoadException => C1OutcomeInspectionFailureKind.TypeLoad,
        MissingMethodException => C1OutcomeInspectionFailureKind.MissingMethod,
        MissingFieldException => C1OutcomeInspectionFailureKind.MissingField,
        Microsoft.Extensions.Options.OptionsValidationException => C1OutcomeInspectionFailureKind.OptionsInvalid,
        _ => C1OutcomeInspectionFailureKind.Other
    };

    private static int? SafeCode(Exception error)
    {
        if (error is KeyLoadException keyLoad && Enum.IsDefined(keyLoad.Code))
        { return (int)keyLoad.Code; }
        if (error.InnerException is Win32Exception native && AllowedNativeCode(native.NativeErrorCode))
        { return native.NativeErrorCode; }
        return null;
    }

    private static bool AllowedCode(C1OutcomeInspectionFailureKind kind, int code)
        => kind == C1OutcomeInspectionFailureKind.KeyLoad ? Enum.IsDefined((ErrorCode)code) : AllowedNativeCode(code);

    private static bool AllowedNativeCode(int code)
        => code is NativeOperationDenied or NativeMissingPath or NativeIoFailure or NativeBadFileDescriptor
            or NativeTryAgain or NativeAccessDenied or NativeResourceBusy or NativePathIsNotDirectory
            or NativeInvalidArgument or NativeOpenFileLimit or NativeNoSpace or NativeBlockingConflict or NativeLinkLoop;
}
