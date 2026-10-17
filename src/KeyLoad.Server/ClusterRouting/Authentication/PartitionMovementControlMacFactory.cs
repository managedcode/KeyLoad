using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementControlMacFactory
{
    internal static PartitionMovementMac Create(NodeOptions options, ReplicaConfiguration configuration, DatabaseEngine database)
    {
        var control = PhysicalOwnerConfiguredTuples.Control(options, configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, configuration);
        var local = PhysicalOwnerConfiguredTuples.Local(options, configuration);
        if (!PhysicalOwnerEntryValidation.Valid(control) || !PhysicalOwnerEntryValidation.Valid(destination)
            || !PhysicalOwnerEntryValidation.Valid(local))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        var configured = options.PhysicalGroupTrust.Role switch
        {
            PhysicalGroupTrustRole.Control when PhysicalOwnerEntryValidation.SameOwner(local.Owner, control.Owner)
                && !PhysicalOwnerEntryValidation.SameOwner(local.Owner, destination.Owner) => options.PeerSecret,
            PhysicalGroupTrustRole.Destination when PhysicalOwnerEntryValidation.SameOwner(local.Owner, destination.Owner)
                && !PhysicalOwnerEntryValidation.SameOwner(local.Owner, control.Owner) => options.PhysicalGroupTrust.ForeignPeerSecret,
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof)
        };
        var key = Convert.FromBase64String(configured
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof));
        try
        {
            return new(key, database.Limits.MaxBatchBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
