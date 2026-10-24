using System.Net;

namespace Peerfluence.Core.Config;

/// <summary>Playback buffering and the address used for shared stream links.</summary>
public sealed class StreamingSettings
{
    /// <summary>
    /// The ranges the engine and the listener accept. They live here rather than at each use so the
    /// settings screen, the MCP tool, the engine and this class cannot drift to different limits.
    /// </summary>
    public const long MinReadAheadBytes = 1024 * 1024;
    public const long MaxReadAheadBytes = 512L * 1024 * 1024;
    public const int MaxReadAheadSeconds = 3600;
    public const int MaxRequestQueueSeconds = 60;
    public const int MaxWaitTimeoutSeconds = 86400;
    public const int MaxPort = 65535;

    public long ReadAheadBytes { get; set; } = 20L * 1024 * 1024;
    public int ReadAheadSeconds { get; set; } = 30;
    public int RequestQueueSecondsWhileBuffering { get; set; } = 1;
    public int DataWaitTimeoutSeconds { get; set; } = 60;
    public int MetadataWaitTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// A specific local IP address, which a shared link names. Blank means nothing is shared: local
    /// playback always stays on loopback and asking for a shared link is refused.
    /// </summary>
    public string BindAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    /// <summary>
    /// Clamps every stored number into range, and reports whether anything moved.
    /// </summary>
    /// <remarks>
    /// A settings file is editable, so none of these arrive trusted. <see cref="Port"/> is the one
    /// that has to be caught here: the stream listener rejects an out-of-range port outright rather
    /// than clamping it, so a hand-edited 70000 would break every shared link, and the settings
    /// screen would show a clamped value that disagreed with the file until the user pressed Save.
    /// </remarks>
    public bool Normalize()
    {
        var before = (ReadAheadBytes, ReadAheadSeconds, RequestQueueSecondsWhileBuffering,
            DataWaitTimeoutSeconds, MetadataWaitTimeoutSeconds, Port);

        ReadAheadBytes = Math.Clamp(ReadAheadBytes, MinReadAheadBytes, MaxReadAheadBytes);
        ReadAheadSeconds = Math.Clamp(ReadAheadSeconds, 0, MaxReadAheadSeconds);
        RequestQueueSecondsWhileBuffering = Math.Clamp(RequestQueueSecondsWhileBuffering, 0, MaxRequestQueueSeconds);
        DataWaitTimeoutSeconds = Math.Clamp(DataWaitTimeoutSeconds, 0, MaxWaitTimeoutSeconds);
        MetadataWaitTimeoutSeconds = Math.Clamp(MetadataWaitTimeoutSeconds, 0, MaxWaitTimeoutSeconds);
        Port = Math.Clamp(Port, 0, MaxPort);

        return before != (ReadAheadBytes, ReadAheadSeconds, RequestQueueSecondsWhileBuffering,
            DataWaitTimeoutSeconds, MetadataWaitTimeoutSeconds, Port);
    }

    /// <summary>
    /// Whether <paramref name="address"/> is a specific local address a shared link can name.
    /// </summary>
    /// <remarks>
    /// Neither blank nor a wildcard is one. A URL naming every interface names none of them, and a
    /// blank address would quietly produce a loopback link that no other device can reach - which
    /// is not a shared link, however much the button that copied it said it was.
    /// </remarks>
    public static bool IsShareableAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address)
        && IPAddress.TryParse(address.Trim(), out var parsed)
        && !parsed.Equals(IPAddress.Any)
        && !parsed.Equals(IPAddress.IPv6Any)
        && !parsed.Equals(IPAddress.None);
}
