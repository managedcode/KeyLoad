namespace KeyLoad;

/// <summary>One failed public API request without raw paths, query strings, payloads, headers or credentials.</summary>
/// <param name="At">Completion time of the failed request.</param>
/// <param name="Method">Normalized HTTP method; unrecognized methods use a fixed marker.</param>
/// <param name="Route">Matched endpoint route template, or a fixed unmatched marker; never the raw request path.</param>
/// <param name="StatusCode">Final HTTP response status code.</param>
/// <param name="Aborted">Whether the caller aborted the request before completion.</param>
/// <param name="ElapsedMilliseconds">Measured request elapsed milliseconds.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.AdminHttpFailure)]
public sealed record AdminHttpFailure([property: Orleans.Id(0)] DateTimeOffset At, [property: Orleans.Id(1)] string Method, [property: Orleans.Id(2)] string Route, [property: Orleans.Id(3)] int StatusCode, [property: Orleans.Id(4)] bool Aborted,
    [property: Orleans.Id(5)] double ElapsedMilliseconds);
