using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed record AnnLoadedSource(AnnManifest Manifest, PackedAnnIndex Index, AnnSeed Seed);
