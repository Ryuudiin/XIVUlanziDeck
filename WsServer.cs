using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dalamud.Plugin.Services;

namespace XivUlanzi.Server;

/// <summary>Servidor WebSocket local (127.0.0.1) com autenticação por token.</summary>
public sealed class WsServer : IDisposable
{
    private sealed class Client(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendLock { get; } = new(1, 1); // WebSocket não aceita envios concorrentes
    }

    private readonly Configuration config;
    private readonly ActionRouter router;
    private readonly IPluginLog log;
    private readonly HttpListener listener = new();
    private readonly ConcurrentDictionary<Guid, Client> clients = new();
    private readonly CancellationTokenSource cts = new();

    public bool IsRunning => listener.IsListening;

    public WsServer(Configuration config, ActionRouter router, IPluginLog log)
    {
        this.config = config;
        this.router = router;
        this.log = log;
    }

    public void Start()
    {
        try
        {
            // 127.0.0.1 não exige permissão de administrador no Windows.
            listener.Prefixes.Add($"http://127.0.0.1:{config.Port}/");
            listener.Start();
            _ = Task.Run(AcceptLoop);
            log.Information("Servidor WebSocket ouvindo em ws://127.0.0.1:{Port}/", config.Port);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Falha ao iniciar o servidor (porta {Port} em uso?)", config.Port);
        }
    }

    /// <summary>Envia um evento a todos os clientes conectados.</summary>
    public Task BroadcastAsync(string eventName, object? data) =>
        Task.WhenAll(clients.Values.Select(c =>
            SendAsync(c, new ServerEvent { Event = eventName, Data = data })));

    private async Task AcceptLoop()
    {
        while (!cts.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                _ = Task.Run(() => HandleConnection(ctx));
            }
            catch (Exception) when (cts.IsCancellationRequested || !listener.IsListening) { break; }
            catch (Exception ex) { log.Error(ex, "Erro no accept loop"); }
        }
    }

    private async Task HandleConnection(HttpListenerContext ctx)
    {
        // 1) Só WebSocket
        if (!ctx.Request.IsWebSocketRequest) { Reject(ctx, 400); return; }

        // 2) Navegadores sempre enviam Origin; clientes Node (Ulanzi) não.
        //    Rejeitar impede que uma página web qualquer controle seu personagem.
        if (!string.IsNullOrEmpty(ctx.Request.Headers["Origin"])) { Reject(ctx, 403); return; }

        // 3) Token
        if (!TokenValid(ctx.Request.QueryString["token"])) { Reject(ctx, 401); return; }

        var wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
        var id = Guid.NewGuid();
        var client = new Client(wsCtx.WebSocket);
        clients[id] = client;
        log.Information("Cliente conectado: {Id}", id);

        try { await ReceiveLoop(client); }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        catch (Exception ex) { log.Error(ex, "Erro na conexão {Id}", id); }
        finally
        {
            clients.TryRemove(id, out _);
            client.Socket.Dispose();
            log.Information("Cliente desconectado: {Id}", id);
        }
    }

    private async Task ReceiveLoop(Client client)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        while (client.Socket.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await client.Socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    return;
                }
                ms.Write(buffer, 0, result.Count);
                if (ms.Length > 64 * 1024) { // limite de segurança
                    await client.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "too big", CancellationToken.None);
                    return;
                }
            } while (!result.EndOfMessage);

            // Não bloqueia o loop de recepção enquanto a ação executa.
            var json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            _ = Task.Run(() => ProcessMessage(client, json));
        }
    }

    private async Task ProcessMessage(Client client, string json)
    {
        Response response;
        try
        {
            var req = JsonSerializer.Deserialize<Request>(json, Json.Options)
                      ?? throw new JsonException("Mensagem vazia.");
            response = await router.HandleAsync(req);
        }
        catch (JsonException ex)
        {
            response = new Response { Ok = false, Error = $"JSON inválido: {ex.Message}" };
        }
        await SendAsync(client, response);
    }

    private async Task SendAsync(Client client, object message)
    {
        if (client.Socket.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json.Options);

        await client.SendLock.WaitAsync();
        try { await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception ex) { log.Debug(ex, "Falha ao enviar mensagem"); }
        finally { client.SendLock.Release(); }
    }

    private bool TokenValid(string? provided)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(config.Token));
    }

    private static void Reject(HttpListenerContext ctx, int status)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.Close();
    }

    public void Dispose()
    {
        cts.Cancel();
        foreach (var c in clients.Values) c.Socket.Abort();
        if (listener.IsListening) listener.Stop();
        listener.Close();
    }
}
