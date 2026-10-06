using Microsoft.Extensions.Options;

namespace KeyLoad;

/// <summary>Process-wide execution policy for canonical JSON and native serialization helpers.</summary>
[ConfigurationOptions]
public sealed record SerializationExecutionOptions
{
    /// <summary>The process configuration identity.</summary>
    public const string SectionName = "KeyLoad:SerializationExecution";
    /// <summary>The native validation failure detail.</summary>
    public const string ValidationMessage = "The serialization execution settings are invalid.";
    private const int DefaultJsonTextMaximumRetainedArrayBytes = 262_144;
    private const int DefaultJsonTextMaximumArraysPerBucket = 2;
    private const int DefaultFingerprintChunkCharacters = 4_096;
    private const int DefaultDomChunkCharacters = 4_096;
    private const int DefaultCanonicalJsonFlushPendingBytes = 65_536;
    private const int DefaultDigestUtf8StackBytes = 256;
    private const int MinimumPoolCapacity = 1;
    private const int MinimumSurrogateChunkCharacters = 2;

    /// <summary>The maximum retained-array size requested from the native JSON text pool.</summary>
    public int JsonTextMaximumRetainedArrayBytes { get; init; } = DefaultJsonTextMaximumRetainedArrayBytes;
    /// <summary>The retained array count requested per native JSON text pool bucket.</summary>
    public int JsonTextMaximumArraysPerBucket { get; init; } = DefaultJsonTextMaximumArraysPerBucket;
    /// <summary>The string input segment size used by native operation fingerprints.</summary>
    public int FingerprintChunkCharacters { get; init; } = DefaultFingerprintChunkCharacters;
    /// <summary>The transient DOM escaping segment size, preserving complete surrogate pairs.</summary>
    public int DomChunkCharacters { get; init; } = DefaultDomChunkCharacters;
    /// <summary>The pending canonical JSON bytes triggering a flush after one complete value.</summary>
    public int CanonicalJsonFlushPendingBytes { get; init; } = DefaultCanonicalJsonFlushPendingBytes;
    /// <summary>The maximum UTF-8 digest field bytes reserved on the stack before using the native pool.</summary>
    public int DigestUtf8StackBytes { get; init; } = DefaultDigestUtf8StackBytes;

    /// <summary>Whether native capacities and string segments remain within qualified bounds.</summary>
    public bool IsValid()
        => JsonTextMaximumRetainedArrayBytes is >= MinimumPoolCapacity and <= DefaultJsonTextMaximumRetainedArrayBytes
            && JsonTextMaximumArraysPerBucket is >= MinimumPoolCapacity and <= DefaultJsonTextMaximumArraysPerBucket
            && FingerprintChunkCharacters is >= MinimumSurrogateChunkCharacters and <= DefaultFingerprintChunkCharacters
            && DomChunkCharacters is >= MinimumSurrogateChunkCharacters and <= DefaultDomChunkCharacters
            && CanonicalJsonFlushPendingBytes is >= MinimumPoolCapacity and <= DefaultCanonicalJsonFlushPendingBytes
            && DigestUtf8StackBytes is >= MinimumPoolCapacity and <= DefaultDigestUtf8StackBytes;

    /// <summary>Rejects invalid settings before an owner allocates its native resources.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(SerializationExecutionOptions), [ValidationMessage]);
        }
    }
}
