using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using XivUlanzi.Server;

namespace XivUlanzi;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xivulanzi";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chat;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly GearsetService gearsets;
    private readonly WsServer server;

    private int lastGearset = int.MinValue;
    private long nextCheck;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chat,
        IFramework framework,
        IClientState clientState,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chat = chat;
        this.framework = framework;
        this.clientState = clientState;
        this.log = log;

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        pluginInterface.SavePluginConfig(config);

        gearsets = new GearsetService(dataManager, textureProvider);
        var router = new ActionRouter(framework, clientState, gearsets, log);
        server = new WsServer(config, router, log);
        server.Start();

        framework.Update += OnUpdate;

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Mostra a porta e o token do servidor para configurar o plugin do Ulanzi.",
        });
    }

    /// <summary>Avisa os clientes quando o gearset equipado muda (checa 4x por segundo).</summary>
    private void OnUpdate(IFramework fw)
    {
        var now = Environment.TickCount64;
        if (now < nextCheck) return;
        nextCheck = now + 250;

        if (!clientState.IsLoggedIn) { lastGearset = int.MinValue; return; }

        int current;
        try { current = gearsets.Current(); }
        catch (Exception ex) { log.Debug(ex, "Falha ao ler gearset atual"); return; }

        if (current == lastGearset) return;
        lastGearset = current;
        _ = server.BroadcastAsync("gearsetChanged", new { number = current });
    }

    private void OnCommand(string command, string args)
    {
        chat.Print($"[XIVUlanzi] Servidor {(server.IsRunning ? "ativo" : "INATIVO")} em ws://127.0.0.1:{config.Port}/");
        chat.Print($"[XIVUlanzi] Token: {config.Token}");
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        commandManager.RemoveHandler(CommandName);
        server.Dispose();
    }
}
