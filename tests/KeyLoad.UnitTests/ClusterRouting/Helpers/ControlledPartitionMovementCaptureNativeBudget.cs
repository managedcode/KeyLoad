using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Measures the complete original typed handle without dropping descriptor fields or emitting content.</summary>
internal static class ControlledPartitionMovementCaptureNativeBudget
{
    private const long EmptyRecords = 0;
    private const long BoundaryStep = 1;
    private const int SharedFamilies = 1;
    private const string WitnessKind = "NativeMovementCaptureCompleteMetadataBudget";
    private const string OuterReplyQualification = "OPEN: direct source-owner fixture does not execute hosted outer reply";

    internal static async Task AssertAsync(PartitionMovementCaptureHandle handle, int maximumBytes,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var empty = handle.Descriptor.Families.Where(family => family.RecordCount == EmptyRecords).ToArray();
        var expectedEmpty = Convert.ToHexStringLower(SHA256.HashData([]));
        await Assert.That(empty.Length).IsGreaterThan(SharedFamilies);
        foreach (var family in empty)
        {
            await Assert.That(family.Digest).IsEqualTo(expectedEmpty);
        }
        var independent = handle with
        {
            Descriptor = handle.Descriptor with
            {
                Families = handle.Descriptor.Families.Select(family => family with
                {
                    Digest = new string(family.Digest.AsSpan())
                }).ToImmutableArray()
            }
        };
        await Assert.That(JsonDefaults.Serialize(handle).AsSpan().SequenceEqual(JsonDefaults.Serialize(independent))).IsTrue();
        var nativeBytes = NativeSerialization.Measure(handle);
        var independentBytes = NativeSerialization.Measure(independent);
        await Assert.That(nativeBytes).IsLessThanOrEqualTo(maximumBytes);
        await Assert.That(nativeBytes).IsLessThan(independentBytes);
        using var below = new ResultByteCounterStream(checked(nativeBytes - BoundaryStep), token.ThrowIfCancellationRequested);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(handle, below));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var healthy = new ResultByteCounterStream(maximumBytes, token.ThrowIfCancellationRequested);
        NativeSerialization.Serialize(handle, healthy);
        await Assert.That(healthy.Length).IsEqualTo(nativeBytes);
        var bytes = NativeSerialization.Serialize(handle);
        var decoded = NativeSerialization.Deserialize<PartitionMovementCaptureHandle>(bytes);
        await Assert.That(bytes.AsSpan().SequenceEqual(NativeSerialization.Serialize(decoded))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(handle))).IsTrue();
        await Console.Out.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            Kind = WitnessKind,
            MaximumBytes = maximumBytes,
            FullNativeHandleBytes = nativeBytes,
            IndependentDigestHandleBytes = independentBytes,
            DescriptorBytes = NativeSerialization.Measure(handle.Descriptor),
            StandaloneFamiliesBytes = NativeSerialization.Measure(handle.Descriptor.Families),
            StandaloneResourcesBytes = NativeSerialization.Measure(handle.Descriptor.Resources),
            GrainValueBytes = NativeSerialization.Measure(new GrainValue(handle)),
            FamilyCount = handle.Descriptor.Families.Length,
            ResourceCount = handle.Descriptor.Resources.Length,
            SharedEmptyFamilyCount = empty.Length,
            CompleteOuterReplyQualification = OuterReplyQualification
        }));
        token.ThrowIfCancellationRequested();
    }
}
