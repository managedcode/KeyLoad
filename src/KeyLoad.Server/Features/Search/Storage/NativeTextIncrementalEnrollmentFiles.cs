using System.Security.Cryptography;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalEnrollmentFiles
{
    internal static NativeTextIncrementalEnrollment Read(string path, Guid sourceNode,
        ReadExecutionBudget? budget, IOptions<NativeTextExecutionOptions> options)
    {
        var file = Path.Combine(path, NativeTextIncrementalProtocol.EnrollmentFile);
        NativeTextFileIO.VerifyRegularFile(file);
        budget?.ChargeBytes(new FileInfo(file).Length);
        var value = NativeTextFileIO.ReadEnvelope<NativeTextIncrementalEnrollment>(file,
            options.Value.MaximumOwnerReceiptBytes);
        if (value is null || value.FormatVersion != NativeTextIncrementalProtocol.FormatVersion
            || value.BuildCommandId == Guid.Empty
            || value.SourceUpperSequence < NativeTextIncrementalProtocol.InitialSequence
            || !NativeTextIncrementalDigest.IsCanonical(value.ResourceSha256)
            || value.OriginalRequestSha256 is null
            || value.OriginalRequestSha256.Length != SHA256.HashSizeInBytes
            || value.Scope is null || value.Consumer is null
            || value.Consumer.Partition != value.Scope.Partition
            || value.Generation < NativeTextIncrementalProtocol.InitialGeneration
            || value.Placement is null || value.Placement.PhysicalShardId == Guid.Empty
            || value.Placement.Incarnation != value.Scope.Incarnation
            || value.Placement.PlacementEpoch < NativeTextIncrementalProtocol.InitialGeneration
            || value.Placement.VoterIds.IsDefaultOrEmpty
            || value.Placement.VoterIds.Distinct(StringComparer.Ordinal).Count() != value.Placement.VoterIds.Length)
        { throw NativeTextErrors.Corrupt(); }
        foreach (var voter in value.Placement.VoterIds)
        { JsonData.Identifier(voter); }
        NativeTextValidation.ValidateScope(value.Scope, sourceNode);
        budget?.Check();
        return value;
    }

    internal static string? Find(string root, TextIndexMaintenanceRequest request,
        ReadExecutionBudget budget, IOptions<NativeTextExecutionOptions> options)
    {
        _ = NativeTextIncrementalRoot.CheckRoot(root, request.NodeId, options, budget);
        string? selected = null;
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            budget.Check();
            var enrolled = Read(path, request.NodeId, budget, options);
            if (enrolled.Consumer != request.Consumer || enrolled.Generation != request.IndexGeneration)
            { continue; }
            if (selected is not null || enrolled.Scope.Collection != request.Collection
                || enrolled.Scope.Field != request.Field
                || enrolled.Placement.PhysicalShardId != request.Placement.PhysicalShardId
                || enrolled.Placement.Incarnation != request.Placement.Incarnation
                || enrolled.Placement.PlacementEpoch != request.Placement.PlacementEpoch
                || !enrolled.Placement.VoterIds.SequenceEqual(request.Placement.VoterIds, StringComparer.Ordinal))
            { throw NativeTextErrors.Corrupt(); }
            selected = Path.GetFileName(path);
        }
        budget.Check();
        return selected;
    }
}
