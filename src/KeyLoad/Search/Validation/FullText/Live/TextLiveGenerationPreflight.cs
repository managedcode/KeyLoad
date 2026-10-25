using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Search;

internal static class TextLiveGenerationPreflight
{
    internal static TextGeneration? Find(string root, string path, string leaf, Guid sourceNodeId,
        TextGenerationSlot? first, TextGenerationSlot? second, TextGenerationSlot? third)
    {
        TextGeneration? found = null;
        Match(first);
        Match(second);
        Match(third);
        return found;

        void Match(TextGenerationSlot? slot)
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
                throw TextErrors.Ownership();
            }
            if (!generation.Published || generation.CurrentIndex is null)
            {
                return;
            }
            if (found is not null && !ReferenceEquals(found, generation))
            {
                throw TextErrors.Ownership();
            }
            found = generation;
        }
    }

    internal static void Validate(TextGeneration generation, string path, string root, string leaf, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions)
        => TextLiveGenerationMetadata.Validate(generation, path, root, leaf, sourceNodeId, executionOptions: executionOptions);
}
