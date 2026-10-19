namespace KeyLoad.Server;

/// <summary>Sanitized details of one failed public API request; never carries a raw path, query, payload or credential.</summary>
/// <param name="Method">Normalized HTTP method or the fixed unrecognized-method marker.</param>
/// <param name="Route">Matched route template or the fixed unmatched marker.</param>
/// <param name="StatusCode">Final HTTP response status code.</param>
/// <param name="Aborted">Whether the caller aborted the request.</param>
internal readonly record struct AdminHttpFailureDetail(string Method, string Route, int StatusCode, bool Aborted);
