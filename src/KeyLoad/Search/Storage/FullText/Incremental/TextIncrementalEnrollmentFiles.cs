using System.Security.Cryptography;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalEnrollmentFiles
{
    internal static TextIncrementalEnrollment Read(string path, Guid sourceNode,
        ReadExecutionBudget? budget, IOptions<TextExecutionOptions> options)
    {
        var file = Path.Combine(path, TextIncrementalProtocol.EnrollmentFile);
        TextFileIO.VerifyRegularFile(file);
        var value = TextFileIO.ReadEnvelope<TextIncrementalEnrollment>(file,
            options.Value.MaximumOwnerReceiptBytes);
        if (value is null || value.FormatVersion != TextIncrementalProtocol.FormatVersion
            || value.BuildCommandId == Guid.Empty
            || value.SourceUpperSequence < TextIncrementalProtocol.InitialSequence
            || !TextIncrementalDigest.IsCanonical(value.ResourceSha256)
            || value.OriginalRequestSha256 is null
            || value.OriginalRequestSha256.Length != SHA256.HashSizeInBytes
            || value.Scope is null || value.Consumer is null
            || value.Consumer.Partition != value.Scope.Partition
            || value.Generation < TextIncrementalProtocol.InitialGeneration
            || value.Placement is null || value.Placement.PhysicalShardId == Guid.Empty
            || value.Placement.Incarnation != value.Scope.Incarnation)
        {
            throw TextErrors.Corrupt();
        }
        TextValidation.ValidateScope(value.Scope, sourceNode);
        budget?.Check();
        return value;
    }

    internal static string? Find(string root, TextIndexMaintenanceRequest request,
        ReadExecutionBudget budget, IOptions<TextExecutionOptions> options)
    {
        _ = TextIncrementalRoot.CheckRoot(root, request.NodeId, options, budget);
        string? selected = null;
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            budget.Check();
            var enrolled = Read(path, request.NodeId, budget, options);
            if (enrolled.Consumer != request.Consumer || enrolled.Generation != request.IndexGeneration)
            {
                continue;
            }
            if (selected is not null || enrolled.Scope.Collection != request.Collection
                || enrolled.Scope.Field != request.Field
                || enrolled.Placement.PhysicalShardId != request.Placement.PhysicalShardId
                || enrolled.Placement.Incarnation != request.Placement.Incarnation)
            {
                throw TextErrors.Corrupt();
            }
            selected = Path.GetFileName(path);
        }
        budget.Check();
        return selected;
    }
}
