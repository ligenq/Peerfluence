using System;
using System.Threading;
using System.Threading.Tasks;
using PeerSharp.Interfaces;

namespace Peerfluence.Services;

public interface IMediaStreamingService : IDisposable
{
    Task<MediaStreamInfo> StartAsync(ITorrent torrent, int fileIndex, bool share, CancellationToken cancellationToken);
    void Play(MediaStreamInfo stream);
    void EndSession();
}

public sealed record MediaStreamInfo(string Url, string ContentType);
