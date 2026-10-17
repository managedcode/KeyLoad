using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Models;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Commands;

internal static class RuntimeJournalCommandRules
{
    internal static PrincipalRecord ProtectedPrincipal()
        => new(RuntimeJournalProtocol.IdentityId, RuntimeJournalProtocol.SystemTenant, [], [])
        {
            ClusterAdministrator = false,
            PolicyEpoch = RuntimeJournalProtocol.ProtectedPolicyEpoch,
            Revoked = false,
            ExpiresAt = null,
            OwnerId = null,
            RestrictRows = false,
            Projects = []
        };

    internal static RuntimeJournalMutationResult Create(IAtomicTransaction transaction, RuntimeJournalMutation mutation,
        ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RuntimeJournalMutationValidation.RequireCreateShape(mutation, options);
        RuntimeJournalValidation.Metadata(mutation.SetProperties, options);
        var existing = headers.FirstOrDefault(header => header.Name == mutation.JournalName);
        if (existing is not null)
        {
            return new(false, Snapshot(existing));
        }
        if (headers.Length >= options.MaximumJournals)
        {
            Capacity();
        }
        var properties = new Dictionary<string, string>(mutation.SetProperties, StringComparer.Ordinal);
        var quota = RuntimeJournalRecordAccess.RequireQuota(transaction);
        var created = new RuntimeJournalHeaderV1(RuntimeJournalProtocol.CurrentVersion, mutation.JournalName,
            mutation.InstanceId, RuntimeJournalProtocol.InitialOwnerGeneration, RuntimeJournalProtocol.InitialContentRevision,
            RuntimeJournalProtocol.InitialLength, mutation.InstanceId.ToString(RuntimeJournalProtocol.GuidFormat), properties);
        transaction.PutRecord(RuntimeJournalKeys.Header(created.Name), created);
        RuntimeJournalRecordAccess.UpdateQuota(transaction, quota, RuntimeJournalProtocol.JournalCountIncrement,
            RuntimeJournalProtocol.EmptyLength, options);
        return new(true, Snapshot(created));
    }

    internal static RuntimeJournalMutationResult UpdateMetadata(IAtomicTransaction transaction,
        RuntimeJournalMutation mutation, ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RuntimeJournalMutationValidation.RequireMetadataShape(mutation, options);
        var header = RuntimeJournalMutationValidation.RequireHeader(headers, mutation.JournalName);
        if (mutation.ExpectedMetadataETag is not null && mutation.ExpectedMetadataETag != header.MetadataETag)
        {
            Conflict();
        }
        var nextProperties = new Dictionary<string, string>(header.Properties, StringComparer.Ordinal);
        foreach (var property in mutation.RemoveProperties)
        {
            nextProperties.Remove(property);
        }
        foreach (var property in mutation.SetProperties)
        {
            nextProperties[property.Key] = property.Value;
        }
        RuntimeJournalValidation.Metadata(nextProperties, options);
        if (SameProperties(nextProperties, header.Properties))
        {
            return new(false, Snapshot(header));
        }
        var ownerGenerationDelta = RuntimeJournalValidation.EffectiveOwnerGeneration(header.Properties, nextProperties);
        if (ownerGenerationDelta != RuntimeJournalProtocol.EmptyGeneration && header.OwnerGeneration == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalProtocol.Capacity);
        }
        var next = header with
        {
            OwnerGeneration = checked(header.OwnerGeneration + ownerGenerationDelta),
            MetadataETag = RuntimeJournalValidation.NextMetadataETag(header.MetadataETag, nextProperties),
            Properties = nextProperties
        };
        transaction.PutRecord(RuntimeJournalKeys.Header(header.Name), next);
        return new(true, Snapshot(next));
    }

    internal static RuntimeJournalMutationResult Append(IAtomicTransaction transaction,
        RuntimeJournalMutation mutation, ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RuntimeJournalMutationValidation.RequireBodyShape(mutation);
        var header = RuntimeJournalMutationValidation.RequireCapturedHeader(headers, mutation, options);
        if (mutation.Data.IsEmpty)
        {
            return new(false, Snapshot(header));
        }
        var nextLength = checked(header.Length + mutation.Data.Length);
        if (nextLength > options.MaximumJournalBytes)
        {
            Capacity();
        }
        var existing = RuntimeJournalRecordAccess.ReadAll(transaction, header, options);
        var combined = new byte[checked((int)nextLength)];
        existing.CopyTo(combined, RuntimeJournalProtocol.AppendBufferStartOffset);
        mutation.Data.Span.CopyTo(combined.AsSpan(existing.Length));
        return ReplaceBody(transaction, header, combined, options);
    }

    internal static RuntimeJournalMutationResult Replace(IAtomicTransaction transaction,
        RuntimeJournalMutation mutation, ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RuntimeJournalMutationValidation.RequireBodyShape(mutation);
        var header = RuntimeJournalMutationValidation.RequireCapturedHeader(headers, mutation, options);
        if (mutation.Data.Length > options.MaximumJournalBytes)
        {
            Capacity();
        }
        return ReplaceBody(transaction, header, mutation.Data.Span, options);
    }

    internal static RuntimeJournalMutationResult Delete(IAtomicTransaction transaction,
        RuntimeJournalMutation mutation, ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RuntimeJournalMutationValidation.RequireDeleteShape(mutation);
        var header = RuntimeJournalMutationValidation.RequireCapturedHeader(headers, mutation, options);
        RuntimeJournalRecordAccess.DeleteBody(transaction, header.Name, header.Length, options.ChunkBytes);
        transaction.Delete(RuntimeJournalKeys.Header(header.Name));
        RuntimeJournalRecordAccess.UpdateQuota(transaction, RuntimeJournalRecordAccess.RequireQuota(transaction),
            RuntimeJournalProtocol.JournalCountDecrement, -header.Length, options);
        return new(true, null);
    }

    private static RuntimeJournalMutationResult ReplaceBody(IAtomicTransaction transaction,
        RuntimeJournalHeaderV1 header, ReadOnlySpan<byte> data, RuntimeJournalOptions options)
    {
        var quota = RuntimeJournalRecordAccess.RequireQuota(transaction);
        if (header.ContentRevision == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalProtocol.Capacity);
        }
        var delta = data.Length - header.Length;
        if (checked(quota.TotalBytes + delta) > options.MaximumTotalBytes)
        {
            Capacity();
        }
        RuntimeJournalRecordAccess.WriteBody(transaction, header.Name, data, options, header.Length);
        var next = header with
        {
            ContentRevision = checked(header.ContentRevision + RuntimeJournalProtocol.ContentRevisionIncrement),
            Length = data.Length
        };
        transaction.PutRecord(RuntimeJournalKeys.Header(header.Name), next);
        RuntimeJournalRecordAccess.UpdateQuota(transaction, quota, RuntimeJournalProtocol.EmptyCount, delta, options);
        return new(true, Snapshot(next));
    }

    private static RuntimeJournalSnapshot Snapshot(RuntimeJournalHeaderV1 header)
        => new(header.Name, header.InstanceId, header.OwnerGeneration, header.ContentRevision, header.Length,
            header.MetadataETag, new Dictionary<string, string>(header.Properties, StringComparer.Ordinal));

    private static bool SameProperties(Dictionary<string, string> first, Dictionary<string, string> second)
        => first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void Conflict() => throw Errors.Fail(ErrorCode.Conflict, RuntimeJournalProtocol.Conflict);

    private static void Capacity() => throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalProtocol.Capacity);
}
