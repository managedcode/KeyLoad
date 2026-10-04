namespace KeyLoad.Core.Features.DatabaseComposition;

/// <summary>Freezes elapsed time for replicated derivation; numeric work limits remain active.</summary>
internal sealed class CompositionTimeProvider : TimeProvider
{
    private const long FrozenTimestamp = 0;

    internal static CompositionTimeProvider Instance { get; } = new();

    public override long GetTimestamp() => FrozenTimestamp;
}
