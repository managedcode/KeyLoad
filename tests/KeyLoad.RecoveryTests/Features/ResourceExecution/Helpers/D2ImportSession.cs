using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal sealed class D2ImportSession(string root, List<Exception> failures) : IAsyncDisposable
{
    private ControlledPartitionMovementLoopbackListeners? listeners;
    private ControlledPartitionMovementNativeNode? source;
    private ControlledPartitionMovementNativeNode? target;
    private PartitionMovementPeerAdmission? sourceAdmission;
    private PartitionMovementPeerAdmission? targetAdmission;
    private Task? disposal;
    internal string Root => root;
    internal ControlledPartitionMovementNativeNode Source => source!;
    internal ControlledPartitionMovementNativeNode Target => target!;
    internal ControlledPartitionMovementProcessOwners Owners { get; private set; } = null!;
    internal ServerRuntimeOptions SourceRuntime { get; private set; } = null!;
    internal ServerRuntimeOptions TargetRuntime { get; private set; } = null!;
    internal PartitionMovementPeerAdmission SourceAdmission => sourceAdmission!;
    internal PartitionMovementPeerAdmission TargetAdmission => targetAdmission!;
    internal string Caller { get; private set; } = null!;
    internal DateTimeOffset ExpiresAt
    {
        get; private set;
    }
    internal List<(ReplicatedOperation Original, OperationResult Result)> SourceSeeds { get; } = [];

    internal void Open(CancellationToken token, bool unknownTargetOrigin = false)
    {
        Directory.CreateDirectory(root);
        listeners = new();
        Owners = new(listeners);
        source = D2ImportSessionCreation.Node(root, D2ImportCrashProtocol.SourceDirectory, Owners.Control.Owner);
        target = D2ImportSessionCreation.Node(root, D2ImportCrashProtocol.TargetDirectory, Owners.Destination.Owner);
        D2ImportSeed.Execute(this, token, unknownTargetOrigin);
        (SourceRuntime, TargetRuntime, Caller, ExpiresAt) = D2ImportSessionCreation.Runtime(this, listeners);
        sourceAdmission = D2ImportSessionCreation.Admission(Source, SourceRuntime);
        targetAdmission = D2ImportSessionCreation.Admission(Target, TargetRuntime);
    }

    internal void CloseTarget()
    {
        var cleanup = new List<Exception>();
        try
        {
            try
            {
                CloseTargetAdmission(cleanup);
            }
            finally { CloseTarget(cleanup); }
        }
        catch (Exception)
        {
            ServerFailureObserver.ThrowIfAny(cleanup);
            throw;
        }
    }

    internal void ReopenTarget()
        => (target, targetAdmission) = D2ImportSessionCreation.Target(this);

    internal void Cold()
    {
        var cleanup = new List<Exception>();
        try
        {
            try
            {
                ObserveCloseAdmissions(cleanup);
            }
            finally { ObserveCloseNodes(cleanup); }
        }
        catch (Exception)
        {
            ServerFailureObserver.ThrowIfAny(cleanup);
            throw;
        }
        source = D2ImportSessionCreation.Node(root, D2ImportCrashProtocol.SourceDirectory, Owners.Control.Owner);
        target = D2ImportSessionCreation.Node(root, D2ImportCrashProtocol.TargetDirectory, Owners.Destination.Owner);
        sourceAdmission = D2ImportSessionCreation.Admission(Source, SourceRuntime);
        targetAdmission = D2ImportSessionCreation.Admission(Target, TargetRuntime);
    }

    public ValueTask DisposeAsync()
    {
        if (disposal is not null)
        {
            return new(disposal);
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        disposal = completion.Task;
        try
        {
            DisposeCore();
            completion.SetResult();
        }
        catch (AggregateException failure)
        {
            completion.SetException(failure.InnerExceptions);
        }
        return new(disposal);
    }

    private void DisposeCore()
    {
        var cleanup = new List<Exception>();
        try
        {
            try
            {
                try
                {
                    ObserveCloseAdmissions(cleanup);
                }
                finally { ObserveCloseNodes(cleanup); }
            }
            finally { CloseListeners(cleanup); }
        }
        catch (Exception)
        {
            throw new AggregateException(cleanup);
        }
        finally
        {
            failures.AddRange(cleanup);
        }
    }

    private void ObserveCloseAdmissions(List<Exception> cleanup)
    {
        try
        {
            CloseSourceAdmission(cleanup);
        }
        finally { CloseTargetAdmission(cleanup); }
    }

    private void ObserveCloseNodes(List<Exception> cleanup)
    {
        try
        {
            CloseSource(cleanup);
        }
        finally { CloseTarget(cleanup); }
    }

    private void CloseSourceAdmission(List<Exception> cleanup)
    {
        try
        {
            sourceAdmission?.Dispose();
            sourceAdmission = null;
        }
        catch (Exception failure)
        {
            cleanup.Add(failure);
            throw;
        }
    }

    private void CloseTargetAdmission(List<Exception> cleanup)
    {
        try
        {
            targetAdmission?.Dispose();
            targetAdmission = null;
        }
        catch (Exception failure)
        {
            cleanup.Add(failure);
            throw;
        }
    }

    private void CloseSource(List<Exception> cleanup)
    {
        try
        {
            source?.Dispose();
            source = null;
        }
        catch (Exception failure)
        {
            cleanup.Add(failure);
            throw;
        }
    }

    private void CloseTarget(List<Exception> cleanup)
    {
        try
        {
            target?.Dispose();
            target = null;
        }
        catch (Exception failure)
        {
            cleanup.Add(failure);
            throw;
        }
    }

    private void CloseListeners(List<Exception> cleanup)
    {
        try
        {
            listeners?.Dispose();
        }
        catch (Exception failure)
        {
            cleanup.Add(failure);
            throw;
        }
    }
}
