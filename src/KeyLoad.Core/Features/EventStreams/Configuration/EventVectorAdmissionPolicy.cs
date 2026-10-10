using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal sealed class EventVectorAdmissionPolicy(IOptions<DatabaseLimits> options)
{
    private const int EmptyMapSlots = 0;
    private const long EmptyRetainedBytes = 0;
    private const string CorruptCapacity = "The native event vector capacity is inconsistent.";
    private const string ExhaustedCapacity = "The native event vector capacity is exhausted.";

    internal void Require(long currentSlots, long currentBytes, long slotDelta, long byteDelta)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        limits.Validate();
        if (currentSlots < EmptyMapSlots || currentBytes < EmptyRetainedBytes)
        { throw Errors.Fail(ErrorCode.Corruption, CorruptCapacity); }
        var nextSlots = checked(currentSlots + slotDelta);
        var nextBytes = checked(currentBytes + byteDelta);
        if (nextSlots < EmptyMapSlots || nextBytes < EmptyRetainedBytes)
        { throw Errors.Fail(ErrorCode.Corruption, CorruptCapacity); }
        if (nextSlots > limits.MaxResults || nextBytes > limits.MaxQueryReadBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ExhaustedCapacity); }
    }

    internal void RequireEncodedBytes(long encodedBytes)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        limits.Validate();
        if (encodedBytes < EmptyRetainedBytes)
        { throw Errors.Fail(ErrorCode.Corruption, CorruptCapacity); }
        if (encodedBytes > limits.MaxBatchBytes || encodedBytes > limits.MaxQueryReadBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ExhaustedCapacity); }
    }
    internal void RequireEntryCount(int count)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        limits.Validate();
        if (count < EmptyMapSlots)
        { throw Errors.Fail(ErrorCode.Corruption, CorruptCapacity); }
        if (count > limits.MaxResults)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ExhaustedCapacity); }
    }
}
