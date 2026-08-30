using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Core.Services.Rpc;

namespace Peerfluence.Services;

/// <summary>
/// Serves the Transmission RPC endpoint over HTTP, so Sonarr, Radarr and anything else built to
/// drive a torrent client can drive this one.
///
/// <para>
/// <see cref="HttpListener"/> rather than a web framework. This is one route answering one verb, the
/// Windows build compiles ahead of time where a framework is a liability, and a desktop application
/// has no business carrying a web server it uses a hundredth of.
/// </para>
/// </summary>
public sealed class TransmissionRpcHostedService : IHostedService, IDisposable
{
    private const string Path = "/transmission/rpc";

    /// <summary>
    /// Transmission's cross-site protection, and not optional: every client implements the handshake,
    /// so a server that does not answer 409 with a session id is one they cannot talk to. It also
    /// does the job it was built for - a form posted from a web page cannot set this header.
    /// </summary>
    private const string SessionIdHeader = "X-Transmission-Session-Id";

    private readonly ITransmissionRpcHandler _handler;
    private readonly IAppSettingsService _settingsService;
    private readonly ILogger<TransmissionRpcHostedService> _logger;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public TransmissionRpcHostedService(
        ITransmissionRpcHandler handler,
        IAppSettingsService settingsService,
        ILogger<TransmissionRpcHostedService> logger)
    {
        _handler = handler;
        _settingsService = settingsService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current.Remote;
        if (!settings.Enabled)
        {
            return Task.CompletedTask;
        }

        if (!settings.IsUsable)
        {
            // Listening on every interface with no password would hand anyone who can reach the port
            // the ability to add and delete downloads. Refused rather than served.
            _logger.LogWarning(
                "Remote control not started: listening beyond this machine requires a username and password.");
            return Task.CompletedTask;
        }

        // Loopback unless told otherwise, and the prefix is the whole of the access control at this
        // level: bound to localhost, nothing off this machine can reach it at all.
        var host = settings.AllowRemoteConnections ? "+" : "localhost";
        var prefix = $"http://{host}:{settings.Port}{Path}/";

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            // A port in use, or Windows refusing a non-loopback prefix without a URL reservation.
            // Neither is a reason to fail startup: the rest of the application works without this.
            _logger.LogError(ex, "Remote control could not listen on {Prefix}", prefix);
            _listener = null;
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = AcceptAsync(_cts.Token);

        _logger.LogInformation("Remote control listening on {Prefix}", prefix);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _listener?.Close();

        if (_loop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Remote control loop ended with an exception during shutdown");
            }
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _loop = null;
    }

    void IDisposable.Dispose()
    {
        _cts?.Cancel();
        _listener?.Close();
        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _loop = null;
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { IsListening: true } listener)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || _listener is not { IsListening: true })
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remote control failed to accept a request");
                continue;
            }

            // Not awaited: one slow client must not stop the next request being accepted.
            _ = RespondAsync(context, cancellationToken);
        }
    }

    private async Task RespondAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settingsService.Current.Remote;

            if (!IsAuthorised(context, settings))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"Peerfluence\"");
                context.Response.Close();
                return;
            }

            if (!string.Equals(context.Request.Headers[SessionIdHeader], _sessionId, StringComparison.Ordinal))
            {
                // The handshake: answer 409 and hand out the id, and the client repeats the request
                // with it. Every Transmission client does this without being told.
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                context.Response.AddHeader(SessionIdHeader, _sessionId);
                context.Response.Close();
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                context.Response.Close();
                return;
            }

            string body;
            using (var reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            var responseJson = await _handler.HandleAsync(body, cancellationToken).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes(responseJson);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remote control failed to answer a request");
            try
            {
                context.Response.Abort();
            }
            catch (Exception)
            {
                // The client is already gone; nothing to report to.
            }
        }
    }

    private static bool IsAuthorised(HttpListenerContext context, RemoteSettings settings)
    {
        if (!settings.RequiresAuthentication)
        {
            return true;
        }

        var header = context.Request.Headers["Authorization"];
        if (header == null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
        {
            return false;
        }

        // Ordinal, and both halves checked: a comparison that stopped at the username would let
        // anyone in who knew it.
        return string.Equals(decoded[..separator], settings.Username, StringComparison.Ordinal) &&
               string.Equals(decoded[(separator + 1)..], settings.Password, StringComparison.Ordinal);
    }
}
