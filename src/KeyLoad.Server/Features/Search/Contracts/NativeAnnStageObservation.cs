using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed record NativeAnnStageObservation(AnnMaintenanceCapabilityKind Kind, AnnWorkBudget Budget);
