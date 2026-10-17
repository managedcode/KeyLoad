using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Features.Search;

internal static class NativeTextLiveGenerationPreflight
{
    internal static NativeTextGeneration? Find(string root, string path, string leaf, Guid sourceNodeId,
        NativeTextGenerationSlot? first, NativeTextGenerationSlot? second, NativeTextGenerationSlot? third)
    {
        NativeTextGeneration? found = null;
        Match(first);
        Match(second);
        Match(third);
        return found;

        void Match(NativeTextGenerationSlot? slot)
        {
            if (slot?.Generation is not { } generation
                || !StringComparer.Ordinal.Equals(Path.Combine(root, leaf), path)
                || !StringComparer.Ordinal.Equals(generation.Path, path))
            {
                return;
            }
            if (!StringComparer.Ordinal.Equals(generation.Leaf, leaf)
                || generation.Scope.NodeId != sourceNodeId)
            {
                throw NativeTextErrors.Ownership();
            }
            if (!generation.Published || generation.CurrentIndex is null)
            {
                return;
            }
            if (found is not null && !ReferenceEquals(found, generation))
            {
                throw NativeTextErrors.Ownership();
            }
            found = generation;
        }
    }

    internal static void Validate(NativeTextGeneration generation, string path, string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextLiveGenerationMetadata.Validate(generation, path, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
}
