using Dalamud.Game.Command;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace CharacterHeight;

public sealed class Plugin : IDalamudPlugin {
    private readonly Configuration config;
    private readonly WindowSystem windows;
    private readonly ConfigWindow configWindow;
    private readonly HeightOverlay overlay;
    private readonly IFontHandle axisFont;

    public Plugin(IDalamudPluginInterface pluginInterface) {
        pluginInterface.Create<Service>();
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ApplyCutsceneVisibility();

        axisFont = pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, 18f));
        overlay = new HeightOverlay(config, axisFont);

        windows = new WindowSystem("CharacterHeight");
        configWindow = new ConfigWindow(config);
        windows.AddWindow(configWindow);

        pluginInterface.UiBuilder.Draw += OnDraw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;

        AddConfigCommand("/hdc");
        AddConfigCommand("/heightdisplay");
    }

    public void Dispose() {
        Service.Commands.RemoveHandler("/hdc");
        Service.Commands.RemoveHandler("/heightdisplay");
        Service.PluginInterface.UiBuilder.Draw -= OnDraw;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        windows.RemoveAllWindows();
        overlay.Dispose();
        axisFont.Dispose();
    }

    private void OnDraw() {
        ApplyCutsceneVisibility();
        windows.Draw();
        overlay.Draw();
    }

    private void AddConfigCommand(string command) {
        Service.Commands.AddHandler(command, new CommandInfo(OnCommand) {
            HelpMessage = "Open the Height Display config.",
            ShowInHelp = true,
        });
    }

    private void OnCommand(string command, string args) => ToggleConfig();

    private void ToggleConfig() => configWindow.Toggle();

    private void ApplyCutsceneVisibility() {
        Service.PluginInterface.UiBuilder.DisableCutsceneUiHide = !config.HideInCutscene;
    }
}
