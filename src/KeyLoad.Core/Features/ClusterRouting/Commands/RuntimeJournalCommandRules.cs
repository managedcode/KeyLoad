using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Models;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Commands;

internal static class RuntimeJournalCommandRules
{
    internal static void RequireCollections(RuntimeJournalMutation mutation)
    {
        if (mutation.SetProperties is null || mutation.RemoveProperties.IsDefault) Invalid();
    }

    internal static PrincipalRecord ProtectedPrincipal()
        => new(RuntimeJournalProtocol.IdentityId, RuntimeJournalProtocol.SystemTenant, [], [])
        { ClusterAdministrator = false, PolicyEpoch = RuntimeJournalProtocol.ProtectedPolicyEpoch, Revoked = false,
            ExpiresAt = null, OwnerId = null, RestrictRows = false, Projects = [] };

    internal static void RequireBootstrapShape(RuntimeJournalMutation mutation)
    {
        if (mutation.JournalName != string.Empty || mutation.InstanceId != Guid.Empty || mutation.OwnerGeneration != 0
            || mutation.ContentRevision != 0 || mutation.ExpectedMetadataETag is not null || !mutation.Data.IsEmpty
            || mutation.SetProperties.Count != 0 || !mutation.RemoveProperties.IsEmpty)
            Invalid();
    }

    internal static RuntimeJournalMutationResult Create(IAtomicTransaction transaction, RuntimeJournalMutation mutation,
        ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RequireCreateShape(mutation, options);
        RuntimeJournalValidation.Metadata(mutation.SetProperties, options);
        var existing = headers.FirstOrDefault(header => header.Name == mutation.JournalName);
        if (existing is not null) return new(false, Snapshot(existing));
        if (headers.Length >= options.MaximumJournals) Capacity();
        var properties = new Dictionary<string, string>(mutation.SetProperties, StringComparer.Ordinal);
        var quota = RuntimeJournalRecordAccess.RequireQuota(transaction);
        var created = new RuntimeJournalHeaderV1(RuntimeJournalProtocol.CurrentVersion, mutation.JournalName,
            mutation.InstanceId, RuntimeJournalProtocol.InitialOwnerGeneration, RuntimeJournalProtocol.InitialContentRevision,
            RuntimeJournalProtocol.InitialLength, mutation.InstanceId.ToString(RuntimeJournalProtocol.GuidFormat), properties);
        transaction.PutRecord(RuntimeJournalKeys.Header(created.Name), created);
        RuntimeJournalRecordAccess.UpdateQuota(transaction, quota, 1, 0, options);
        return new(true, Snapshot(created));
    }

    internal static RuntimeJournalMutationResult UpdateMetadata(IAtomicTransaction transaction,
        RuntimeJournalMutation mutation, ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RequireMetadataShape(mutation, options);
        var header = RequireHeader(headers, mutation.JournalName);
        if (mutation.ExpectedMetadataETag is not null && mutation.ExpectedMetadataETag != header.MetadataETag) Conflict();
        var nextProperties = new Dictionary<string, string>(header.Properties, StringComparer.Ordinal);
        foreach (var property in mutation.RemoveProperties) nextProperties.Remove(property);
        foreach (var property in mutation.SetProperties) nextProperties[property.Key] = property.Value;
        RuntimeJournalValidation.Metadata(nextProperties, options);
        if (SameProperties(nextProperties, header.Properties)) return new(false, Snapshot(header));
        var ownerGenerationDelta = RuntimeJournalValidation.EffectiveOwnerGeneration(header.Properties, nextProperties);
        if (ownerGenerationDelta != 0 && header.OwnerGeneration == long.MaxValue)
            throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalProtocol.Capacity);
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
        RequireBodyShape(mutation);
        var header = RequireCapturedHeader(headers, mutation, options);
        if (mutation.Data.IsEmpty) return new(false, Snapshot(header));
        var nextLength = checked(header.Length + mutation.Data.Length);
        if (nextLength > options.MaximumJournalBytes) Capacity();
        var existing = RuntimeJournalRecordAccess.ReadAll(transaction, header, options);
        var combined = new byte[checked((int)nextLength)];
        existing.CopyTo(combined, 0);
        mutation.Data.Span.CopyTo(combined.AsSpan(existing.Length));
        return ReplaceBody(transaction, header, combined, options);
    }

    internal static RuntimeJournalMutationResult Replace(IAtomicTransaction transaction,
        RuntimeJournalMutation mutation, ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RequireBodyShape(mutation);
        var header = RequireCapturedHeader(headers, mutation, options);
        if (mutation.Data.Length > options.MaximumJournalBytes) Capacity();
        return ReplaceBody(transaction, header, mutation.Data.Span, options);
    }

    internal static RuntimeJournalMutationResult Delete(IAtomicTransaction transaction,
        RuntimeJournalMutation mutation, ImmutableArray<RuntimeJournalHeaderV1> headers, RuntimeJournalOptions options)
    {
        RequireDeleteShape(mutation);
        var header = RequireCapturedHeader(headers, mutation, options);
        RuntimeJournalRecordAccess.DeleteBody(transaction, header.Name, header.Length, options.ChunkBytes);
        transaction.Delete(RuntimeJournalKeys.Header(header.Name));
        RuntimeJournalRecordAccess.UpdateQuota(transaction, RuntimeJournalRecordAccess.RequireQuota(transaction), -1, -header.Length, options);
        return new(true, null);
    }

    internal static void RequireCaptured(RuntimeJournalHeaderV1 header, Guid instanceId,
        long ownerGeneration, long contentRevision)
    {
        if (header.InstanceId != instanceId || header.OwnerGeneration != ownerGeneration
            || header.ContentRevision != contentRevision) Conflict();
    }

    private static RuntimeJournalMutationResult ReplaceBody(IAtomicTransaction transaction,
        RuntimeJournalHeaderV1 header, ReadOnlySpan<byte> data, RuntimeJournalOptions options)
    {
        var quota = RuntimeJournalRecordAccess.RequireQuota(transaction);
        if (header.ContentRevision == long.MaxValue)
            throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalProtocol.Capacity);
        var delta = data.Length - header.Length;
        if (checked(quota.TotalBytes + delta) > options.MaximumTotalBytes) Capacity();
        RuntimeJournalRecordAccess.WriteBody(transaction, header.Name, data, options, header.Length);
        var next = header with { ContentRevision = checked(header.ContentRevision + 1), Length = data.Length };
        transaction.PutRecord(RuntimeJournalKeys.Header(header.Name), next);
        RuntimeJournalRecordAccess.UpdateQuota(transaction, quota, 0, delta, options);
        return new(true, Snapshot(next));
    }

    private static void RequireCreateShape(RuntimeJournalMutation mutation, RuntimeJournalOptions options)
    {
        RuntimeJournalValidation.Name(mutation.JournalName, options);
        if (mutation.InstanceId == Guid.Empty || mutation.OwnerGeneration != 0 || mutation.ContentRevision != 0
            || mutation.ExpectedMetadataETag is not null || !mutation.Data.IsEmpty || !mutation.RemoveProperties.IsEmpty)
            Invalid();
    }

    private static void RequireMetadataShape(RuntimeJournalMutation mutation, RuntimeJournalOptions options)
    {
        RuntimeJournalValidation.Name(mutation.JournalName, options);
        if (!mutation.Data.IsEmpty || mutation.SetProperties is null || mutation.RemoveProperties.IsDefault
            || mutation.RemoveProperties.Distinct(StringComparer.Ordinal).Count() != mutation.RemoveProperties.Length
            || mutation.RemoveProperties.Any(mutation.SetProperties.ContainsKey)) Invalid();
        foreach (var key in mutation.RemoveProperties) RuntimeJournalValidation.MetadataKey(key, options);
    }

    private static void RequireBodyShape(RuntimeJournalMutation mutation)
    {
        if (mutation.InstanceId == Guid.Empty || mutation.OwnerGeneration < 1 || mutation.ContentRevision < 0
            || mutation.ExpectedMetadataETag is not null || mutation.SetProperties.Count != 0 || !mutation.RemoveProperties.IsEmpty)
            Invalid();
    }

    private static void RequireDeleteShape(RuntimeJournalMutation mutation)
    {
        if (mutation.InstanceId == Guid.Empty || mutation.OwnerGeneration < 1 || mutation.ContentRevision < 0
            || mutation.ExpectedMetadataETag is not null || !mutation.Data.IsEmpty
            || mutation.SetProperties.Count != 0 || !mutation.RemoveProperties.IsEmpty) Invalid();
    }

    private static RuntimeJournalHeaderV1 RequireCapturedHeader(ImmutableArray<RuntimeJournalHeaderV1> headers,
        RuntimeJournalMutation mutation, RuntimeJournalOptions options)
    {
        RuntimeJournalValidation.Name(mutation.JournalName, options);
        var header = RequireHeader(headers, mutation.JournalName);
        RequireCaptured(header, mutation.InstanceId, mutation.OwnerGeneration, mutation.ContentRevision);
        return header;
    }

    private static RuntimeJournalHeaderV1 RequireHeader(ImmutableArray<RuntimeJournalHeaderV1> headers, string name)
        => headers.FirstOrDefault(header => header.Name == name)
            ?? throw Errors.Fail(ErrorCode.NotFound, RuntimeJournalProtocol.Missing);

    private static RuntimeJournalSnapshot Snapshot(RuntimeJournalHeaderV1 header)
        => new(header.Name, header.InstanceId, header.OwnerGeneration, header.ContentRevision, header.Length,
            header.MetadataETag, new Dictionary<string, string>(header.Properties, StringComparer.Ordinal));

    private static bool SameProperties(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
        => first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void Invalid() => throw Errors.Fail(ErrorCode.Validation, RuntimeJournalProtocol.InvalidRequest);
    private static void Conflict() => throw Errors.Fail(ErrorCode.Conflict, RuntimeJournalProtocol.Conflict);
    private static void Capacity() => throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalProtocol.Capacity);
}
