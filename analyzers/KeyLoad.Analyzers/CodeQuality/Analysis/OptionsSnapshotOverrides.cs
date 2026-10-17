using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class OptionsSnapshotOverrides
{
    internal static bool IsHardcoded(Compilation compilation, IWithOperation clone, CancellationToken cancellationToken) =>
        clone.Initializer?.Initializers.OfType<ISimpleAssignmentOperation>().Any(assignment =>
            HardcodedDurationPolicy.IsHardcodedOptionsOverride(compilation, assignment.Value, cancellationToken)) == true;
}
