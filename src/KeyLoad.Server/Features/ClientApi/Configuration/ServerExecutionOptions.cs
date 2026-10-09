namespace KeyLoad.Server;

/// <summary>Centrally configured readiness, shutdown and public HTTP resource admission.</summary>
[ConfigurationOptions]
internal sealed class ServerExecutionOptions
{
    /// <summary>The server execution configuration section.</summary>
    public const string SectionName = "KeyLoad:ServerExecution";
    /// <summary>The startup rejection for settings outside accepted server bounds.</summary>
    public const string ValidationMessage = "Server deadlines, HTTP body admission, JSON depth and the explicit HTTP/2 port must be within accepted bounds.";

    private const int DefaultReadySeconds = 10;
    private const int DefaultShutdownSeconds = 30;
    private const int MaximumDeadlineMinutes = 2;
    private const int MaximumHttpBodyBytes = 33_554_432;
    private const int MaximumSupportedJsonDepth = 64;
    private const int MinimumPositiveCount = 1;
    private const int DefaultHttp2Port = 8081;
    private const int MaximumTcpPort = 65535;
    private static readonly TimeSpan MaximumDeadline = TimeSpan.FromMinutes(MaximumDeadlineMinutes);

    /// <summary>Gets or sets the cancellation deadline for a readiness check.</summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(DefaultReadySeconds);
    /// <summary>Gets or sets the observation deadline for joined process shutdown.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(DefaultShutdownSeconds);
    /// <summary>Gets or sets the maximum public HTTP request body bytes admitted by Kestrel.</summary>
    public int MaximumBodyBytes { get; set; } = MaximumHttpBodyBytes;
    /// <summary>Gets or sets the JSON parser nesting depth within the unchanged public protocol ceiling.</summary>
    public int MaximumJsonDepth { get; set; } = MaximumSupportedJsonDepth;

    /// <summary>Gets or sets whether a dedicated native HTTP/2 listener is explicitly enabled.</summary>
    public bool EnableHttp2 { get; set; }
    /// <summary>Gets or sets the dedicated HTTP/2 listener port without changing existing listener addresses.</summary>
    public int Http2Port { get; set; } = DefaultHttp2Port;

    /// <summary>Checks execution durations and public parser bounds before admitting work.</summary>
    /// <returns>Whether configured values preserve the accepted server limits.</returns>
    public bool IsValid() => IsBounded(ReadyTimeout) && IsBounded(ShutdownTimeout)
        && MaximumBodyBytes is >= MinimumPositiveCount and <= MaximumHttpBodyBytes
        && MaximumJsonDepth is >= MinimumPositiveCount and <= MaximumSupportedJsonDepth
        && Http2Port is >= MinimumPositiveCount and <= MaximumTcpPort;

    private static bool IsBounded(TimeSpan duration)
        => duration > TimeSpan.Zero && duration <= MaximumDeadline;
}
