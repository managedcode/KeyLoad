namespace KeyLoad.UnitTests.Features.Authorization;

/// <summary>Unsupported historical live input; signed with the existing actual native database signer.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ResourceLifetimeCursorNegativeProtocol.LiveAlias)]
internal sealed record ResourceLifetimeLiveCursorNegativeV1(
    [property: Orleans.Id(ResourceLifetimeCursorNegativeProtocol.PurposeField)] string Purpose,
    [property: Orleans.Id(ResourceLifetimeCursorNegativeProtocol.QueryHashField)] string QueryHash,
    [property: Orleans.Id(ResourceLifetimeCursorNegativeProtocol.ChangeCursorField)] string ChangeCursor);
