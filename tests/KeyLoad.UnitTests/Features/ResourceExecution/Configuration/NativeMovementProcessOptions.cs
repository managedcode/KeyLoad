namespace KeyLoad.UnitTests;

/// <summary>Owns the existing bounded whole-process recovery policy for movement fixture execution.</summary>
[ConfigurationOptions]
internal sealed class NativeMovementProcessOptions
{
    private const int MaximumOperationSeconds = 90;
    private const int MaximumCleanupSeconds = 30;
    private const int MaximumOutputCharacters = 8192;
    private const int MaximumReadCharacters = 256;
    private const int MaximumIssuedGrantExpirySeconds = 5;
    private const string Invalid = "Native movement process execution exceeds its bounded fixture policy.";

    internal TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(MaximumOperationSeconds);
    internal TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(MaximumCleanupSeconds);
    internal int OutputCharacters { get; init; } = MaximumOutputCharacters;
    internal int ReadCharacters { get; init; } = MaximumReadCharacters;
    internal TimeSpan IssuedGrantExpiryWindow { get; init; } = TimeSpan.FromSeconds(MaximumIssuedGrantExpirySeconds);

    internal void Validate()
    {
        if (OperationTimeout <= TimeSpan.Zero || OperationTimeout.Ticks > MaximumOperationSeconds * TimeSpan.TicksPerSecond
            || CleanupTimeout <= TimeSpan.Zero || CleanupTimeout.Ticks > MaximumCleanupSeconds * TimeSpan.TicksPerSecond
            || IssuedGrantExpiryWindow <= TimeSpan.Zero
            || IssuedGrantExpiryWindow.Ticks > MaximumIssuedGrantExpirySeconds * TimeSpan.TicksPerSecond
            || OutputCharacters <= 0 || OutputCharacters > MaximumOutputCharacters
            || ReadCharacters <= 0 || ReadCharacters > MaximumReadCharacters || ReadCharacters > OutputCharacters)
        { throw new InvalidOperationException(Invalid); }
    }
}
