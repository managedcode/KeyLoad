using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static OperationResult ExecuteRegisterPhysicalOwner(IAtomicTransaction transaction,
        PrincipalRecord principal, RegisterPhysicalOwnerV1 request)
    {
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PhysicalOwnerDirectoryProtocol.AdministratorRequired); }
        PhysicalOwnerDirectoryValidation.ValidateRegistration(request);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(transaction)
            ?? throw Errors.Fail(ErrorCode.NotFound, PhysicalOwnerDirectoryProtocol.Missing);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, request.Control.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerDirectoryProtocol.ControlMismatch); }
        var current = PhysicalOwnerDirectorySerialization.Read(transaction);
        if (current is not null && !PhysicalOwnerEntryValidation.SameOwner(current.ControlOwner, catalog.DefaultShard))
        { throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.ControlMismatch); }
        if (current is not null && !PhysicalOwnerEntryValidation.Same(current.Owners.Single(entry =>
            entry.Owner.PhysicalShardId == current.ControlOwner.PhysicalShardId), request.Control))
        { throw Errors.Fail(ErrorCode.Conflict, PhysicalOwnerDirectoryProtocol.IdentityConflict); }
        if ((current?.Revision ?? PhysicalOwnerDirectoryProtocol.EmptyRevision) != request.ExpectedRevision)
        { throw Errors.Fail(ErrorCode.Conflict, PhysicalOwnerDirectoryProtocol.StaleRevision); }
        var owners = current?.Owners ?? [request.Control];
        var existing = owners.FirstOrDefault(entry => entry.Owner.PhysicalShardId == request.Destination.Owner.PhysicalShardId);
        if (existing is not null)
        {
            if (!PhysicalOwnerEntryValidation.Same(existing, request.Destination))
            { throw Errors.Fail(ErrorCode.Conflict, PhysicalOwnerDirectoryProtocol.IdentityConflict); }
            return Result(current!);
        }
        return StoreRegisteredOwner(transaction, request, owners);
    }

    private static OperationResult StoreRegisteredOwner(IAtomicTransaction transaction,
        RegisterPhysicalOwnerV1 request, ImmutableArray<RegisteredPhysicalOwnerV1> owners)
    {
        if (owners.Length >= PhysicalOwnerDirectoryProtocol.MaximumOwners)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PhysicalOwnerDirectoryProtocol.Capacity); }
        var nextOwners = owners.Add(request.Destination);
        if (!PhysicalOwnerDirectoryValidation.Unique(nextOwners))
        { throw Errors.Fail(ErrorCode.Conflict, PhysicalOwnerDirectoryProtocol.IdentityConflict); }
        if (request.ExpectedRevision == long.MaxValue)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PhysicalOwnerDirectoryProtocol.RevisionExhausted); }
        var next = new PhysicalOwnerDirectoryV1(PhysicalOwnerDirectoryProtocol.Version,
            checked(request.ExpectedRevision + PhysicalOwnerDirectoryProtocol.RevisionStep), request.Control.Owner, nextOwners);
        transaction.Put(PhysicalOwnerDirectorySerialization.Key(), PhysicalOwnerDirectorySerialization.Encode(next));
        return Result(next);
    }
}
