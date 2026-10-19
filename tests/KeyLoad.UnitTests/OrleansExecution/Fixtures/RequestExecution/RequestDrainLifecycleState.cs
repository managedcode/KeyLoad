using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

/// <summary>Owns the physical fixture until original request settlement; borrows the independently verified request owner.</summary>
internal sealed class RequestDrainLifecycleState : IDisposable
{
    private const int NoFailures = 0;
    private const string Unjoined = "The original execution request work has not settled; retain its database and root.";
    private bool disposalAttempted;

    internal RequestDrainLifecycleState(RequestWorkOwner requests, TimeProvider clock,
        List<Exception> failures, bool callbackFailure, CancellationToken token)
    {
        Requests = requests;
        Clock = clock;
        Failures = failures;
        Token = token;
        ExpectsFailure = callbackFailure;
        Database = new(timeProvider: clock);
        try
        {
            Original = new(Guid.NewGuid(), Database.Partition,
                [new PutDocument(RequestDrainLifecycleProtocol.Collection, RequestDrainLifecycleProtocol.Document,
                    RequestDrainLifecycleProtocol.OriginalJson)]);
        }
        catch (Exception primary)
        {
            var openingFailures = new List<Exception> { primary };
            ServerFailureObserver.Observe(Database.JoinOwnedResources, openingFailures);
            ServerFailureObserver.ThrowIfAny(openingFailures);
            throw;
        }
    }

    internal TestDatabase Database
    {
        get;
    }
    internal RequestWorkOwner Requests
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }
    internal List<Exception> Failures
    {
        get;
    }
    internal CancellationToken Token
    {
        get;
    }
    internal bool ExpectsFailure
    {
        get;
    }
    internal InvalidOperationException CallbackFailure
    {
        get;
    } = new(RequestDrainLifecycleProtocol.CallbackFailure);
    internal List<Exception> DrainFailures
    {
        get;
    } = [];
    internal List<Exception> DisposalFailures
    {
        get;
    } = [];
    internal bool DrainObserved
    {
        get;
        set;
    }
    internal Task? Verification
    {
        get;
        set;
    }
    internal Task? Drain
    {
        get;
        set;
    }
    internal RuntimeTestOptions Timing
    {
        get;
    } = new();
    internal SiloLifecycleSubject Lifecycle
    {
        get;
    } = new(NullLogger<SiloLifecycleSubject>.Instance);
    internal TaskCompletionSource Entered
    {
        get;
    } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Canceled
    {
        get;
    } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release
    {
        get;
    } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal List<string> Events
    {
        get;
    } = [];
    internal Task? Producer
    {
        get;
        set;
    }
    internal Task? Stopping
    {
        get;
        set;
    }
    internal CommandRequest Original
    {
        get;
    }
    internal CommitReceipt Receipt
    {
        get;
        set;
    } = null!;
    internal string[] Image
    {
        get;
        set;
    } = [];
    internal long Position
    {
        get;
        set;
    }

    /// <summary>Appends physical cleanup failures to the caller's original ledger; the flow throws after lexical disposal.</summary>
    public void Dispose()
    {
        if (disposalAttempted)
        {
            return;
        }
        disposalAttempted = true;
        var joined = Requests.IsJoined && Drain is { IsCompleted: true } && Verification is { IsCompleted: true }
            && Producer is null or { IsCompleted: true } && Stopping is null or { IsCompleted: true };
        if (Failures.Count == NoFailures && (!joined || Verification is not { IsCompletedSuccessfully: true }))
        {
            Failures.Add(new InvalidOperationException(Unjoined));
        }
        if (!joined)
        {
            return;
        }
        try
        {
            if (Failures.Count == NoFailures)
            {
                Database.Dispose();
            }
            else
            {
                Database.JoinOwnedResources();
            }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            Failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            Failures.Add(failure);
        }
    }
}
