using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Observes whether the process still matches an already captured process identity.</summary>
internal static class IsolatedAggregateNodeIdentityObservation
{
    private const string ConfirmationFailed = "The process identity probe and exit confirmation both failed.";

    internal static bool IsSameProcessRunning(Process process, long expectedStartTimeUtcTicks)
    {
        try
        {
            return ObserveIdentity(process, expectedStartTimeUtcTicks);
        }
        catch (Win32Exception probeFailure)
        {
            return ConfirmExitAfterProbeFailure(process, probeFailure);
        }
    }

    private static bool ObserveIdentity(Process process, long expectedStartTimeUtcTicks)
    {
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != expectedStartTimeUtcTicks)
        {
            return false;
        }

        return !process.HasExited;
    }

    private static bool ConfirmExitAfterProbeFailure(Process process, Win32Exception probeFailure)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }
        }
        catch (Exception confirmationFailure)
        {
            throw CreateBothFailures(probeFailure, confirmationFailure);
        }

        ExceptionDispatchInfo.Capture(probeFailure).Throw();
        return false;
    }

    private static AggregateException CreateBothFailures(Win32Exception probeFailure, Exception confirmationFailure)
    {
        var fatal = CqrsRuntimeFailures.FindFatal(confirmationFailure);
        if (fatal is not null)
        {
            return new AggregateException(ConfirmationFailed, confirmationFailure, probeFailure);
        }

        return new AggregateException(ConfirmationFailed, probeFailure, confirmationFailure);
    }
}
