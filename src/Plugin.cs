using System.Numerics;
using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace NativeDof;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool CameraFocus = true;
    public float Distance = 5;
    public float Aperture = 8;
    public bool DisableInCombat = true;
}

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState Client { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] internal static ISigScanner Scanner { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    private readonly Configuration config;
    private readonly NativeBackend backend;
    private readonly WindowSystem windows = new("NativeDof");
    private readonly ControlWindow window;
    private bool enabled;
    private string activity = "Off";
    private string lastReportedActivity = "";
    private long nextDiagnosticAt;
    private int pendingDiagnostics;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new();
        var clean = RenderSettings.Create(false, config.CameraFocus, config.Distance, config.Aperture);
        config.Distance = clean.Distance;
        config.Aperture = clean.Aperture;
        backend = new NativeBackend(Interop, Scanner, Log);
        window = new ControlWindow(this);
        windows.AddWindow(window);
        Commands.AddHandler("/nativedof", new CommandInfo(OnCommand) { HelpMessage = "Native DoF: open controls, or on / off / status." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;
        Framework.Update += Update;
    }

    private void Open() => window.IsOpen = true;
    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "on": enabled = backend.Available; Open(); break;
            case "off": enabled = false; backend.Publish(RenderSettings.Off); break;
            default: Open(); break;
        }
        Update(Framework);
        if (arguments.Trim().Equals("status", StringComparison.OrdinalIgnoreCase)) backend.ReportDiagnostics(activity);
    }

    private void Update(IFramework framework)
    {
        var blocked = !Client.IsLoggedIn || Client.IsGPosing
            || Conditions[ConditionFlag.WatchingCutscene]
            || Conditions[ConditionFlag.WatchingCutscene78]
            || Conditions[ConditionFlag.OccupiedInCutSceneEvent]
            || Conditions[ConditionFlag.BetweenAreas]
            || Conditions[ConditionFlag.BetweenAreas51]
            || Conditions[ConditionFlag.LoggingOut];
        var combat = config.DisableInCombat && Conditions[ConditionFlag.InCombat];
        var firstPerson = !blocked && backend.IsFirstPerson;
        activity = !backend.Available ? "Unavailable" : !enabled ? "Off"
            : blocked ? "Suspended: cutscene, GPose, loading or logged out"
            : firstPerson ? "Suspended: first-person camera"
            : combat ? "Suspended: combat" : "Enabled for gameplay";
        backend.Publish(RenderSettings.Create(enabled && backend.Available && !blocked && !combat && !firstPerson,
            config.CameraFocus, config.Distance, config.Aperture));
        if (activity != lastReportedActivity)
        {
            lastReportedActivity = activity;
            backend.ReportDiagnostics(activity);
            pendingDiagnostics = enabled && backend.Available && !blocked && !combat && !firstPerson ? 3 : 0;
            nextDiagnosticAt = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
        }
        else if (pendingDiagnostics > 0 && Stopwatch.GetTimestamp() >= nextDiagnosticAt)
        {
            backend.ReportDiagnostics(activity);
            pendingDiagnostics--;
            nextDiagnosticAt = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
        }
    }

    public void Dispose()
    {
        enabled = false;
        backend.ReportDiagnostics("Unloading");
        Framework.Update -= Update;
        backend.Dispose();
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        Commands.RemoveHandler("/nativedof");
        windows.RemoveAllWindows();
    }

    private sealed class ControlWindow : Window
    {
        private readonly Plugin owner;
        public ControlWindow(Plugin plugin) : base("Native DoF Prototype 0.1.3###NativeDof")
        {
            owner = plugin;
            Size = new Vector2(510, 360);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            var p = owner;
            ImGui.TextUnformatted(p.activity);
            ImGui.TextWrapped("Experimental native gameplay DoF. Starts off on every load. Suspends automatically in first-person. Cutscenes and GPose use the game's own settings.");
            ImGui.BeginDisabled(!p.backend.Available);
            if (ImGui.Checkbox("Enable gameplay depth of field", ref p.enabled)) p.Update(Framework);
            ImGui.EndDisabled();
            var changed = ImGui.Checkbox("Focus at camera look-at point (native)", ref p.config.CameraFocus);
            if (!p.config.CameraFocus)
                changed |= ImGui.SliderFloat("Focus distance", ref p.config.Distance, 0.5f, 100, "%.2f");
            changed |= ImGui.SliderFloat("Aperture (lower = more blur)", ref p.config.Aperture, 1.4f, 32, "f/%.1f");
            changed |= ImGui.Checkbox("Suspend during combat", ref p.config.DisableInCombat);
            if (changed) { PluginInterface.SavePluginConfig(p.config); p.Update(Framework); }
            if (ImGui.Button("Turn off now")) { p.enabled = false; p.backend.Publish(RenderSettings.Off); p.Update(Framework); }
            ImGui.Separator();
            ImGui.TextWrapped(p.backend.Status);
            ImGui.TextUnformatted($"Render calls: {p.backend.RenderCalls:N0} | Overrides: {p.backend.AppliedCalls:N0}");
            ImGui.TextUnformatted($"World scene calls: {p.backend.WorldSceneCalls:N0} | Resource skips: {p.backend.ResourceSkips:N0}");
            ImGui.TextUnformatted($"Scene overrides: {p.backend.SceneOverrides:N0} | Blur pass disabled: {p.backend.CocDisabled}");
            ImGui.TextUnformatted($"Blur pass focus: {p.backend.EffectiveFocus:F2} | Aperture: {p.backend.EffectiveAperture:F1}");
            ImGui.TextUnformatted($"Last view: 0x{p.backend.LastView:X} (world view: 0x1E)");
            ImGui.TextWrapped("First test: outdoors, stationary, out of combat. Try f/2.8, then compare on/off while zooming. /nativedof off disables immediately.");
        }
    }
}
