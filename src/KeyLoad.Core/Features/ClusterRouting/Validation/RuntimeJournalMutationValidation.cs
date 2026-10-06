using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Models;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class RuntimeJournalMutationValidation
{
    internal static void RequireCollections(RuntimeJournalMutation mutation)
    {
        if (mutation.SetProperties is null || mutation.RemoveProperties.IsDefault)
        {
            Invalid();
        }
    }

    internal static void RequireBootstrapShape(RuntimeJournalMutation mutation)
    {
        if (!string.IsNullOrEmpty(mutation.JournalName) || mutation.InstanceId != Guid.Empty
            || mutation.OwnerGeneration != RuntimeJournalProtocol.EmptyGeneration
            || mutation.ContentRevision != RuntimeJournalProtocol.EmptyRevision || mutation.ExpectedMetadataETag is not null
            || !mutation.Data.IsEmpty || mutation.SetProperties.Count != RuntimeJournalProtocol.EmptyCount
            || !mutation.RemoveProperties.IsEmpty)
        {
            Invalid();
        }
    }

    internal static void RequireCreateShape(RuntimeJournalMutation mutation, RuntimeJournalOptions options)
    {
        RuntimeJournalValidation.Name(mutation.JournalName, options);
        if (mutation.InstanceId == Guid.Empty || mutation.OwnerGeneration != RuntimeJournalProtocol.EmptyGeneration
            || mutation.ContentRevision != RuntimeJournalProtocol.EmptyRevision
            || mutation.ExpectedMetadataETag is not null || !mutation.Data.IsEmpty || !mutation.RemoveProperties.IsEmpty)
        {
            Invalid();
        }
    }

    internal static void RequireMetadataShape(RuntimeJournalMutation mutation, RuntimeJournalOptions options)
    {
        RuntimeJournalValidation.Name(mutation.JournalName, options);
        if (!mutation.Data.IsEmpty || mutation.SetProperties is null || mutation.RemoveProperties.IsDefault
            || mutation.RemoveProperties.Distinct(StringComparer.Ordinal).Count() != mutation.RemoveProperties.Length
            || mutation.RemoveProperties.Any(mutation.SetProperties.ContainsKey))
        {
            Invalid();
        }
        foreach (var key in mutation.RemoveProperties)
        {
            RuntimeJournalValidation.MetadataKey(key, options);
        }
    }

    internal static void RequireBodyShape(RuntimeJournalMutation mutation)
    {
        if (mutation.InstanceId == Guid.Empty || mutation.OwnerGeneration < RuntimeJournalProtocol.InitialOwnerGeneration
            || mutation.ContentRevision < RuntimeJournalProtocol.EmptyRevision || mutation.ExpectedMetadataETag is not null
            || mutation.SetProperties.Count != RuntimeJournalProtocol.EmptyCount || !mutation.RemoveProperties.IsEmpty)
        {
            Invalid();
        }
    }

    internal static void RequireDeleteShape(RuntimeJournalMutation mutation)
    {
        if (mutation.InstanceId == Guid.Empty || mutation.OwnerGeneration < RuntimeJournalProtocol.InitialOwnerGeneration
            || mutation.ContentRevision < RuntimeJournalProtocol.EmptyRevision || mutation.ExpectedMetadataETag is not null
            || !mutation.Data.IsEmpty || mutation.SetProperties.Count != RuntimeJournalProtocol.EmptyCount
            || !mutation.RemoveProperties.IsEmpty)
        {
            Invalid();
        }
    }

    internal static RuntimeJournalHeaderV1 RequireCapturedHeader(ImmutableArray<RuntimeJournalHeaderV1> headers,
        RuntimeJournalMutation mutation, RuntimeJournalOptions options)
    {
        RuntimeJournalValidation.Name(mutation.JournalName, options);
        var header = RequireHeader(headers, mutation.JournalName);
        RequireCaptured(header, mutation.InstanceId, mutation.OwnerGeneration, mutation.ContentRevision);
        return header;
    }

    internal static void RequireCaptured(RuntimeJournalHeaderV1 header, Guid instanceId,
        long ownerGeneration, long contentRevision)
    {
        if (header.InstanceId != instanceId || header.OwnerGeneration != ownerGeneration
            || header.ContentRevision != contentRevision)
        {
            Conflict();
        }
    }

    internal static RuntimeJournalHeaderV1 RequireHeader(ImmutableArray<RuntimeJournalHeaderV1> headers, string name)
        => headers.FirstOrDefault(header => header.Name == name)
            ?? throw Errors.Fail(ErrorCode.NotFound, RuntimeJournalProtocol.Missing);

    private static void Invalid() => throw Errors.Fail(ErrorCode.Validation, RuntimeJournalProtocol.InvalidRequest);

    private static void Conflict() => throw Errors.Fail(ErrorCode.Conflict, RuntimeJournalProtocol.Conflict);
}
