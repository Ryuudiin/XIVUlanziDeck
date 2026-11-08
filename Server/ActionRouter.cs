using System.Text.Json;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace XivUlanzi.Server;

/// <summary>
/// Mapeia o nome da ação para um handler. Tudo que toca o jogo roda na thread
/// principal (IFramework.RunOnFrameworkThread): NUNCA chame o jogo direto do socket.
/// </summary>
public sealed class ActionRouter
{
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly GearsetService gearsets;
    private readonly IPluginLog log;
    private readonly Dictionary<string, Func<JsonElement?, Task<object?>>> handlers = new();

    public ActionRouter(IFramework framework, IClientState clientState, GearsetService gearsets, IPluginLog log)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.gearsets = gearsets;
        this.log = log;

        Register("ping", _ => Task.FromResult<object?>("pong"));
        Register("getPlayerInfo", _ => OnFramework(GetPlayerInfo));
        Register("executeCommand", data => OnFramework(() => ExecuteCommand(data)));

        // Gearsets
        Register("listGearsets", _ => OnFramework(() => (object?)gearsets.List()
            .Select(g => new { number = g.Number, name = g.Name, classJob = g.ClassJob, abbreviation = g.Abbreviation })
            .ToList()));
        Register("getCurrentGearset", _ => OnFramework(() => (object?)new { number = gearsets.Current() }));
        Register("switchGearset", data => OnFramework(() => SwitchGearset(data)));
        Register("getJobIcon", data =>
        {
            var job = GetInt(data, "classJob");
            // Leitura de arquivo/PNG fora da thread do jogo, para não travar frames.
            return Task.Run(() => (object?)new { png = gearsets.GetJobIconBase64(job) });
        });
    }

    public void Register(string action, Func<JsonElement?, Task<object?>> handler) => handlers[action] = handler;

    public async Task<Response> HandleAsync(Request req)
    {
        if (!handlers.TryGetValue(req.Action, out var handler))
            return new Response { Id = req.Id, Ok = false, Error = $"Ação desconhecida: '{req.Action}'" };

        try
        {
            var result = await handler(req.Data);
            return new Response { Id = req.Id, Ok = true, Data = result };
        }
        catch (Exception ex)
        {
            log.Error(ex, "Erro na ação {Action}", req.Action);
            return new Response { Id = req.Id, Ok = false, Error = ex.Message };
        }
    }

    private Task<object?> OnFramework(Func<object?> work) => framework.RunOnFrameworkThread(work);

    private static int GetInt(JsonElement? data, string name)
    {
        if (data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        }
        throw new ArgumentException($"Parâmetro '{name}' ausente ou inválido.");
    }

    // ---------- Handlers ----------

    private object? GetPlayerInfo()
    {
        if (!clientState.IsLoggedIn)
            throw new InvalidOperationException("Jogador não está logado.");

        return new { loggedIn = true, territory = clientState.TerritoryType };
    }

    private object? SwitchGearset(JsonElement? data)
    {
        var number = GetInt(data, "number");
        if (number < 1 || number > GearsetService.MaxGearsets)
            throw new ArgumentException($"Número de gearset deve estar entre 1 e {GearsetService.MaxGearsets}.");

        // Mesmo comando que o jogador digitaria; respeita as restrições normais do jogo (combate etc.).
        SendToChatBox($"/gearset change {number}");
        return new { number };
    }

    private object? ExecuteCommand(JsonElement? data)
    {
        var text = data?.GetProperty("command").GetString();

        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('/') || text.Length > 500)
            throw new ArgumentException("Comando inválido (deve começar com '/' e ter até 500 caracteres).");

        SendToChatBox(text);
        return new { executed = text };
    }

    private static unsafe void SendToChatBox(string text)
    {
        var str = Utf8String.FromString(text);
        try { UIModule.Instance()->ProcessChatBoxEntry(str); }
        finally { str->Dtor(true); }
    }
}
