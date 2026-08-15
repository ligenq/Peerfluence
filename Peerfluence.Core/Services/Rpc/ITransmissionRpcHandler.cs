namespace Peerfluence.Core.Services.Rpc;

/// <summary>
/// Turns one Transmission RPC request into its response.
///
/// <para>
/// Deliberately string in, string out: the protocol is JSON over a single endpoint, and keeping the
/// transport out of this leaves the whole of the behaviour testable without a socket.
/// </para>
/// </summary>
public interface ITransmissionRpcHandler
{
    Task<string> HandleAsync(string requestJson, CancellationToken cancellationToken = default);
}
