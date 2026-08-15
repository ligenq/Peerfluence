using System.Text.Json.Serialization;

namespace Peerfluence.Core.Config;

/// <summary>
/// The remote-control endpoint: a Transmission-compatible RPC interface, so that the tools built
/// around torrent clients - Sonarr, Radarr and the rest - can drive this one.
///
/// <para>
/// Off by default, and bound to this machine unless told otherwise. This is the only part of
/// Peerfluence that listens for instructions rather than for peers, so the defaults are the careful
/// ones and turning it on is a decision the user makes rather than one they inherit.
/// </para>
/// </summary>
public sealed class RemoteSettings
{
    /// <summary>Transmission's own port, so existing clients need no configuring beyond the address.</summary>
    public const int DefaultPort = 9091;

    public bool Enabled { get; set; }

    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// Whether to listen on every interface rather than only on this machine.
    ///
    /// <para>
    /// Off by default. A loopback-only listener can be reached by what is already running here,
    /// which is the case this exists for; opening it to the network is a different decision, and one
    /// that should not be made silently by a default.
    /// </para>
    /// </summary>
    public bool AllowRemoteConnections { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    [JsonIgnore]
    public bool RequiresAuthentication => !string.IsNullOrWhiteSpace(Username);

    /// <summary>
    /// Whether the settings describe something safe to start. Listening on every interface without a
    /// password would hand anyone who can reach the port the ability to add and delete downloads, so
    /// that combination is refused rather than served.
    /// </summary>
    [JsonIgnore]
    public bool IsUsable => !AllowRemoteConnections || RequiresAuthentication;
}
