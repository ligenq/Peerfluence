using System.Threading.Tasks;

namespace Peerfluence.Services;

public interface IAddTorrentDialogService
{
    Task<bool> ShowTorrentFileAsync(string torrentPath);

    Task<bool> ShowMagnetAsync(string magnetUri);

    Task<bool> ShowSslCertificateAsync(PeerSharp.Interfaces.ITorrent torrent);
}
