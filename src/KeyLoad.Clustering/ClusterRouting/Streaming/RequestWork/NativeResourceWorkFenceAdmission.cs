using System.Collections.Immutable;

namespace KeyLoad.Clustering;

internal sealed record NativeResourceWorkFenceAdmission(ImmutableArray<byte> Fingerprint, NativeResourceWorkSnapshot Snapshot);
