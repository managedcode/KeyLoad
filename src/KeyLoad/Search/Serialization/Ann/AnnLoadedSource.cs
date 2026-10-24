using KeyLoad.Core.Search;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal sealed record AnnLoadedSource(AnnManifest Manifest, PackedAnnIndex Index, AnnSeed Seed);
