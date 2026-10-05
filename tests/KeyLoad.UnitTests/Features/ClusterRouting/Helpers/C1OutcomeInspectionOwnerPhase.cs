using System.ComponentModel;
using System.Globalization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;
using KeyLoad.Storage.IO;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum C1OutcomeInspectionOwnerRole
{
    ChildSettled,
    ExplicitOuter,
    ExplicitDatabase,
    FinalOuter,
    FinalDatabase
}

internal enum C1OutcomeInspectionOwnerState
{
    Begin,
    Success,
    Failure,
    Observed
}

internal static class C1OutcomeInspectionOwnerPhase
{
    private const string Prefix = "C1_OUTCOME_OWNER_PHASE v=1";
    private const int OwnerProbeBufferBytes = 1024;
    private const int EagainLinux = 11;
    private const int EagainMac = 35;

    internal static FileStream Acquire(C1OutcomeInspectionFixture fixture,
        C1OutcomeInspectionOwnerRole role, string path)
    {
        fixture.RecordOwnerPhase(Line(role, C1OutcomeInspectionOwnerState.Begin));
        try
        {
            var stream = OfflineRegularFile.Open(path, FileAccess.ReadWrite, FileShare.None, OwnerProbeBufferBytes);
            fixture.RecordOwnerPhase(Line(role, C1OutcomeInspectionOwnerState.Success));
            return stream;
        }
        catch (Exception error) when (!C1OutcomeInspectionFailures.ContainsFatal(error))
        {
            fixture.RecordOwnerPhase(Line(role, C1OutcomeInspectionOwnerState.Failure, SafeCode(error)));
            throw;
        }
        catch (Exception error) when (C1OutcomeInspectionFailures.ContainsFatal(error))
        {
            fixture.RecordOwnerPhase(Line(role, C1OutcomeInspectionOwnerState.Failure));
            throw;
        }
    }

    internal static void ObserveChild(C1OutcomeInspectionFixture fixture, C1OutcomeInspectionProcessResult result)
        => fixture.RecordOwnerPhase(ChildLine(result));

    internal static bool IsAllowedBusyError(IOException error)
        => error.InnerException is Win32Exception native
            && native.NativeErrorCode == (OperatingSystem.IsMacOS() ? EagainMac : EagainLinux);

    private static string ChildLine(C1OutcomeInspectionProcessResult result)
        => $"{Prefix} role=ChildSettled phase=Observed ProcessReaped={Flag(result.ProcessReaped)} "
            + $"InputWriterSettled={Flag(result.InputWriterSettled)} "
            + $"StandardOutputReaderSettled={Flag(result.StandardOutputReaderSettled)} "
            + $"StandardErrorReaderSettled={Flag(result.StandardErrorReaderSettled)} "
            + $"ProcessHandleClosed={Flag(result.ProcessHandleClosed)} "
            + $"OuterOwnerReleased={Flag(result.OuterOwnerReleased)}";

    private static string Line(C1OutcomeInspectionOwnerRole role,
        C1OutcomeInspectionOwnerState state, int? errorCode = null)
    {
        var line = $"{Prefix} role={role} phase={state}";
        return errorCode is { } code ? $"{line} code={code.ToString(CultureInfo.InvariantCulture)}" : line;
    }

    private static int? SafeCode(Exception error)
    {
        if (error is KeyLoadException keyLoad)
        { return (int)keyLoad.Code; }
        if (error.InnerException is Win32Exception native && AllowedNativeError(native.NativeErrorCode))
        { return native.NativeErrorCode; }
        return null;
    }

    private static bool AllowedNativeError(int code)
        => code is 1 or 2 or 5 or 9 or 11 or 13 or 16 or 20 or 22 or 24 or 28 or 35 or 40;

    private static int Flag(bool value) => value ? 1 : 0;
}
