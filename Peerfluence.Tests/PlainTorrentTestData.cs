using System.Security.Cryptography;
using PeerSharp.Core;

namespace Peerfluence.Tests;

/// <summary>
/// The smallest ordinary torrent: one byte, one piece, no SSL. The counterpart to
/// <see cref="SslTorrentTestData"/> for tests that need to tell the two apart.
/// </summary>
internal static class PlainTorrentTestData
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "BEP 3 requires SHA-1 piece hashes.")]
    public static TorrentFile Metadata()
    {
        using var bytes = new MemoryStream();
        bytes.Write("d4:infod6:lengthi1e4:name5:plain12:piece lengthi16384e6:pieces20:"u8);
        bytes.Write(SHA1.HashData([0]));
        bytes.Write("ee"u8);
        return TorrentFile.Parse(bytes.ToArray());
    }
}
