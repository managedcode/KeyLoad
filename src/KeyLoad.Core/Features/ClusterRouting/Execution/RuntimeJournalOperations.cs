using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Models;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Executes bounded runtime journal changes inside caller-owned native transactions.</summary>
public sealed class RuntimeJournalOperations
{
    private readonly DatabaseEngine database;
    private readonly RuntimeJournalOptions options;

    /// <summary>Creates operations using one validated immutable policy snapshot.</summary>
    public RuntimeJournalOperations(DatabaseEngine database, IOptions<RuntimeJournalOptions> configured)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(configured);
        options = configured.Value with { };
        if (!options.IsValid()) throw new ArgumentException(RuntimeJournalOptions.ValidationMessage, nameof(configured));
        this.database = database;
    }

    /// <summary>Applies one private action in the atomic transaction owned by the request path.</summary>
    public RuntimeJournalMutationResult Execute(IAtomicTransaction transaction, PrincipalRecord principal,
        RuntimeJournalMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(mutation);
        RuntimeJournalCommandRules.RequireCollections(mutation);
        RuntimeJournalIdentity.RequireOperation(principal, OperationKind.RuntimeJournal, mutation);
        return mutation.Action == RuntimeJournalAction.BootstrapIdentity
            ? Bootstrap(transaction, principal, mutation)
            : ExecuteJournalMutation(transaction, principal, mutation);
    }

    /// <summary>Reads one bounded journal header after resolving persisted journal authority.</summary>
    public RuntimeJournalSnapshot? GetHeader(string principalId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeJournalValidation.Name(name, options);
        return database.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireJournalPrincipal(view, principalId);
            EnsureBootstrapped(view);
            var headers = RuntimeJournalRecordAccess.ReadCatalog(view, options, cancellationToken);
            return headers.FirstOrDefault(header => header.Name == name) is { } found ? Snapshot(found) : null;
        });
    }

    /// <summary>Reads one page pinned to the requested journal instance and content revision.</summary>
    public RuntimeJournalPage Read(string principalId, RuntimeJournalReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeJournalValidation.Name(request.JournalName, options);
        return database.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireJournalPrincipal(view, principalId);
            EnsureBootstrapped(view);
            var headers = RuntimeJournalRecordAccess.ReadCatalog(view, options, cancellationToken);
            var header = headers.FirstOrDefault(candidate => candidate.Name == request.JournalName)
                ?? throw Errors.Fail(ErrorCode.NotFound, RuntimeJournalProtocol.Missing);
            RuntimeJournalCommandRules.RequireCaptured(header, request.InstanceId, request.OwnerGeneration, request.ContentRevision);
            return RuntimeJournalRecordAccess.ReadPage(view, header, request.Offset, options.ChunkBytes, cancellationToken);
        });
    }

    /// <summary>Reads the entire bounded catalog or fails without returning a partial list.</summary>
    public RuntimeJournalCatalog Catalog(string principalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return database.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireJournalPrincipal(view, principalId);
            EnsureBootstrapped(view);
            return new RuntimeJournalCatalog(RuntimeJournalRecordAccess.ReadCatalog(view, options, cancellationToken)
                .Select(Snapshot).ToImmutableArray());
        });
    }

    private RuntimeJournalMutationResult Bootstrap(IAtomicTransaction transaction, PrincipalRecord principal,
        RuntimeJournalMutation mutation)
    {
        RuntimeJournalCommandRules.RequireBootstrapShape(mutation);
        var current = database.Principal(transaction, principal.Id, database.EvaluationClock.GetUtcNow());
        if (!current.ClusterAdministrator || current.Id == RuntimeJournalIdentity.ProtectedPrincipalId)
            throw Errors.Fail(ErrorCode.PermissionDenied, RuntimeJournalProtocol.InvalidRequest);
        var principalKey = KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId);
        var existingPrincipal = RuntimeJournalRecordAccess.ReadRecord<PrincipalRecord>(transaction, principalKey);
        var marker = RuntimeJournalRecordAccess.ReadRecord<RuntimeJournalCatalogMarkerV1>(transaction, RuntimeJournalKeys.Catalog());
        var quota = RuntimeJournalRecordAccess.ReadRecord<RuntimeJournalQuotaV1>(transaction, RuntimeJournalKeys.Quota());
        if (existingPrincipal is not null || marker is not null || quota is not null)
        {
            if (MatchesBootstrap(existingPrincipal, marker, quota))
            {
                _ = RuntimeJournalRecordAccess.ReadCatalog(transaction, options, default);
                return new(false, null);
            }
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        RuntimeJournalRecordAccess.RequireNoRows(transaction);
        transaction.PutRecord(principalKey, RuntimeJournalCommandRules.ProtectedPrincipal());
        transaction.PutRecord(RuntimeJournalKeys.Catalog(), new RuntimeJournalCatalogMarkerV1(RuntimeJournalProtocol.CurrentVersion));
        transaction.PutRecord(RuntimeJournalKeys.Quota(), new RuntimeJournalQuotaV1(RuntimeJournalProtocol.CurrentVersion,
            RuntimeJournalProtocol.InitialJournalCount, RuntimeJournalProtocol.InitialTotalBytes));
        return new(true, null);
    }

    private RuntimeJournalMutationResult ExecuteJournalMutation(IAtomicTransaction transaction,
        PrincipalRecord principal, RuntimeJournalMutation mutation)
    {
        RequireJournalPrincipal(transaction, principal.Id);
        EnsureBootstrapped(transaction);
        var headers = RuntimeJournalRecordAccess.ReadCatalog(transaction, options, default);
        return mutation.Action switch
        {
            RuntimeJournalAction.Create => RuntimeJournalCommandRules.Create(transaction, mutation, headers, options),
            RuntimeJournalAction.UpdateMetadata => RuntimeJournalCommandRules.UpdateMetadata(transaction, mutation, headers, options),
            RuntimeJournalAction.Append => RuntimeJournalCommandRules.Append(transaction, mutation, headers, options),
            RuntimeJournalAction.Replace => RuntimeJournalCommandRules.Replace(transaction, mutation, headers, options),
            RuntimeJournalAction.Delete => RuntimeJournalCommandRules.Delete(transaction, mutation, headers, options),
            _ => throw Errors.Fail(ErrorCode.Validation, RuntimeJournalProtocol.InvalidRequest)
        };
    }

    private void RequireJournalPrincipal(IKeyValueView view, string principalId)
    {
        var persisted = database.Principal(view, principalId, database.EvaluationClock.GetUtcNow());
        RuntimeJournalIdentity.RequireProtected(persisted);
    }

    private static void EnsureBootstrapped(IKeyValueView view)
    {
        var marker = RuntimeJournalRecordAccess.ReadRecord<RuntimeJournalCatalogMarkerV1>(view, RuntimeJournalKeys.Catalog());
        var quota = RuntimeJournalRecordAccess.ReadRecord<RuntimeJournalQuotaV1>(view, RuntimeJournalKeys.Quota());
        var principal = RuntimeJournalRecordAccess.ReadRecord<PrincipalRecord>(view,
            KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId));
        if (marker?.Version != RuntimeJournalProtocol.CurrentVersion || quota?.Version != RuntimeJournalProtocol.CurrentVersion
            || principal is null)
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        RuntimeJournalIdentity.RequireProtected(principal);
    }

    private static bool MatchesBootstrap(PrincipalRecord? principal, RuntimeJournalCatalogMarkerV1? marker,
        RuntimeJournalQuotaV1? quota)
    {
        if (principal is null || marker?.Version != RuntimeJournalProtocol.CurrentVersion
            || quota?.Version != RuntimeJournalProtocol.CurrentVersion) return false;
        try { RuntimeJournalIdentity.RequireProtected(principal); return true; }
        catch (KeyLoadException) { return false; }
    }

    private static RuntimeJournalSnapshot Snapshot(RuntimeJournalHeaderV1 header)
        => new(header.Name, header.InstanceId, header.OwnerGeneration, header.ContentRevision, header.Length,
            header.MetadataETag, header.Properties);
}
