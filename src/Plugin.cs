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
    public int Version { get; set; } = 2;
    public bool CameraFocus = true;
    public float Distance = 5;
    public float Aperture = 8;
    public bool DisableInCombat = true;
    public bool CombatCameraFocus = true;
    public float CombatDistance = 5;
    public float CombatAperture = 16;
    public bool DisableInDuty;
    public bool DisableWhileMounted;
    public float ActivationDelay;
    public bool RememberEnabled;
    public bool SavedEnabled;
    public bool Background;
    public float BlurStart = 15;
    public float BlurEnd = 60;
    public float Strength = 0.15f;
    public bool CombatBackground;
    public float CombatBlurStart = 15;
    public float CombatBlurEnd = 60;
    public float CombatStrength = 0.15f;
    public bool CloseLookAt;
    public float CloseDistance = 2;
    public float CloseAperture = 1.4f;
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
    private readonly ActivationPolicy activation = new();
    private readonly CloseFocusPolicy closeFocus = new();
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
        if (config.Version < 2)
        {
            config.CombatCameraFocus = config.CameraFocus;
            config.CombatDistance = config.Distance;
            config.CombatAperture = config.Aperture;
            config.Version = 2;
        }
        var combatClean = RenderSettings.Create(false, config.CombatCameraFocus, config.CombatDistance, config.CombatAperture);
        config.CombatDistance = combatClean.Distance;
        config.CombatAperture = combatClean.Aperture;
        config.ActivationDelay = ActivationPolicy.CleanDelay(config.ActivationDelay);
        backend = new NativeBackend(Interop, Scanner, Log);
        enabled = config.RememberEnabled && config.SavedEnabled && backend.Available;
        window = new ControlWindow(this);
        windows.AddWindow(window);
        Commands.AddHandler("/nativedof", new CommandInfo(OnCommand) { HelpMessage = "Native DoF: controls, on / off / toggle / subtle / balanced / strong / status." });
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
            case "on": SetEnabled(true); break;
            case "off": SetEnabled(false); break;
            case "toggle": SetEnabled(!enabled); break;
            case "subtle": ApplyPreset(16); break;
            case "balanced": ApplyPreset(8); break;
            case "strong": ApplyPreset(2.8f); break;
            default: Open(); break;
        }
        Update(Framework);
        if (arguments.Trim().Equals("status", StringComparison.OrdinalIgnoreCase)) backend.ReportDiagnostics(activity);
    }

    private void Save()
    {
        config.SavedEnabled = config.RememberEnabled && enabled;
        PluginInterface.SavePluginConfig(config);
        Update(Framework);
    }

    private void SetEnabled(bool value)
    {
        enabled = value && backend.Available;
        if (!enabled) backend.Publish(RenderSettings.Off);
        Save();
    }

    private void ApplyPreset(float aperture, bool combat = false)
    {
        if (combat)
        {
            config.CombatBackground = true;
            config.CombatBlurStart = 15;
            config.CombatBlurEnd = 60;
            config.CombatStrength = aperture == 16 ? 0.15f : aperture == 8 ? 0.35f : 0.65f;
        }
        else
        {
            config.Background = true;
            config.BlurStart = 15;
            config.BlurEnd = 60;
            config.Strength = aperture == 16 ? 0.15f : aperture == 8 ? 0.35f : 0.65f;
        }
        Save();
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
        var firstPerson = !blocked && backend.IsFirstPerson;
        var duty = Conditions[ConditionFlag.BoundByDuty] || Conditions[ConditionFlag.BoundByDuty56]
            || Conditions[ConditionFlag.BoundByDuty95];
        var mounted = Conditions[ConditionFlag.Mounted] || Conditions[ConditionFlag.RidingPillion]
            || Conditions[ConditionFlag.MountOrOrnamentTransition] || Conditions[ConditionFlag.InFlight];
        var inCombat = Conditions[ConditionFlag.InCombat];
        activity = activation.Evaluate(new(backend.Available, enabled, blocked, firstPerson,
            inCombat, duty, mounted), config.DisableInCombat,
            config.DisableInDuty, config.DisableWhileMounted, config.ActivationDelay,
            (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        var apply = activity == ActivationPolicy.Active;
        var profile = RenderSettings.Create(apply,
            inCombat ? config.CombatCameraFocus : config.CameraFocus,
            inCombat ? config.CombatDistance : config.Distance,
            inCombat ? config.CombatAperture : config.Aperture,
            inCombat ? config.CombatBackground : config.Background,
            inCombat ? config.CombatBlurStart : config.BlurStart,
            inCombat ? config.CombatBlurEnd : config.BlurEnd,
            inCombat ? config.CombatStrength : config.Strength);
        backend.Publish(closeFocus.Select(profile, config.CloseLookAt,
            apply && config.CloseLookAt ? backend.CameraDistance : float.NaN,
            config.CloseDistance, config.CloseAperture));
        if (apply) activity = closeFocus.Active ? "Active: close-up look-at focus"
            : inCombat ? "Active: combat profile" : "Active: out-of-combat profile";
        if (activity != lastReportedActivity)
        {
            lastReportedActivity = activity;
            backend.ReportDiagnostics(activity);
            pendingDiagnostics = apply ? 3 : 0;
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
        public ControlWindow(Plugin plugin) : base("Native DoF Prototype 0.1.6###NativeDof")
        {
            owner = plugin;
            Size = new Vector2(560, 540);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            var p = owner;
            ImGui.TextUnformatted(p.activity);
            ImGui.TextWrapped("Native gameplay DoF. First-person, cutscenes, GPose and loading always suspend the effect.");
            ImGui.BeginDisabled(!p.backend.Available);
            var requested = p.enabled;
            if (ImGui.Checkbox("Enable gameplay depth of field", ref requested)) p.SetEnabled(requested);
            ImGui.EndDisabled();
            var changed = ImGui.Checkbox("Remember enabled state between sessions", ref p.config.RememberEnabled);
            changed |= ImGui.Checkbox("Focus at look-at point when zoomed in close", ref p.config.CloseLookAt);
            if (p.config.CloseLookAt)
            {
                changed |= ImGui.SliderFloat("Close-up distance", ref p.config.CloseDistance, 0.5f, 10, "%.2f");
                changed |= ImGui.SliderFloat("Close-up aperture", ref p.config.CloseAperture, 1.4f, 32, "f/%.1f");
                ImGui.TextWrapped("Temporarily replaces either profile with native look-at focus. Defaults to strong f/1.4 within 2 camera-distance units. Returns to your profile 0.5 units beyond the threshold. Suspension rules still apply.");
            }
            ImGui.Separator();
            ImGui.TextUnformatted("Out-of-combat profile");
            changed |= DrawProfile(false, ref p.config.CameraFocus, ref p.config.Distance, ref p.config.Aperture);
            ImGui.TextWrapped("Presets use the background blur curve. Distances are measured from the camera, not the player. Ctrl-click a slider to type an exact value.");
            ImGui.Separator();
            changed |= ImGui.Checkbox("Suspend during combat", ref p.config.DisableInCombat);
            if (!p.config.DisableInCombat)
            {
                ImGui.TextUnformatted("Combat profile");
                changed |= DrawProfile(true, ref p.config.CombatCameraFocus, ref p.config.CombatDistance, ref p.config.CombatAperture);
            }
            ImGui.Separator();
            changed |= ImGui.Checkbox("Suspend inside duties", ref p.config.DisableInDuty);
            changed |= ImGui.Checkbox("Suspend while mounted or flying", ref p.config.DisableWhileMounted);
            changed |= ImGui.SliderFloat("Activation delay (seconds)", ref p.config.ActivationDelay, 0, 30, "%.1f s");
            ImGui.TextWrapped("Wait this long after enabling or after all suspension rules clear. Suspension is immediate; the delay does not fade the blur.");
            if (changed)
            {
                var clean = RenderSettings.Create(false, p.config.CameraFocus, p.config.Distance, p.config.Aperture);
                p.config.Distance = clean.Distance;
                p.config.Aperture = clean.Aperture;
                var combatClean = RenderSettings.Create(false, p.config.CombatCameraFocus, p.config.CombatDistance, p.config.CombatAperture);
                p.config.CombatDistance = combatClean.Distance;
                p.config.CombatAperture = combatClean.Aperture;
                p.config.ActivationDelay = ActivationPolicy.CleanDelay(p.config.ActivationDelay);
                p.config.CloseDistance = CloseFocusPolicy.CleanDistance(p.config.CloseDistance);
                p.config.CloseAperture = RenderSettings.Create(false, true, 5, p.config.CloseAperture).Aperture;
                p.Save();
            }
            if (ImGui.Button("Turn off now")) p.SetEnabled(false);
            ImGui.Separator();
            if (!ImGui.CollapsingHeader("Diagnostics")) return;
            ImGui.TextWrapped(p.backend.Status);
            ImGui.TextUnformatted($"Render calls: {p.backend.RenderCalls:N0} | Overrides: {p.backend.AppliedCalls:N0}");
            ImGui.TextUnformatted($"World scene calls: {p.backend.WorldSceneCalls:N0} | Resource skips: {p.backend.ResourceSkips:N0}");
            ImGui.TextUnformatted($"Scene overrides: {p.backend.SceneOverrides:N0} | Blur pass disabled: {p.backend.CocDisabled}");
            ImGui.TextUnformatted($"Blur pass focus: {p.backend.EffectiveFocus:F2} | Aperture: {p.backend.EffectiveAperture:F1}");
            ImGui.TextUnformatted($"Last view: 0x{p.backend.LastView:X} (world view: 0x1E)");
            ImGui.TextWrapped("First test: outdoors, stationary, out of combat. Try f/2.8, then compare on/off while zooming. /nativedof off disables immediately.");
        }

        private bool DrawProfile(bool combat, ref bool cameraFocus, ref float distance, ref float aperture)
        {
            ImGui.PushID(combat ? "combat" : "exploration");
            if (ImGui.Button("Subtle")) owner.ApplyPreset(16, combat);
            ImGui.SameLine();
            if (ImGui.Button("Balanced")) owner.ApplyPreset(8, combat);
            ImGui.SameLine();
            if (ImGui.Button("Strong")) owner.ApplyPreset(2.8f, combat);
            ref var background = ref (combat ? ref owner.config.CombatBackground : ref owner.config.Background);
            var changed = ImGui.Checkbox("Background blur curve", ref background);
            if (background)
            {
                ref var start = ref (combat ? ref owner.config.CombatBlurStart : ref owner.config.BlurStart);
                ref var end = ref (combat ? ref owner.config.CombatBlurEnd : ref owner.config.BlurEnd);
                ref var strength = ref (combat ? ref owner.config.CombatStrength : ref owner.config.Strength);
                changed |= ImGui.SliderFloat("Blur begins at", ref start, 1, 499, "%.1f");
                changed |= ImGui.SliderFloat("Full blur at", ref end, 1.5f, 500, "%.1f");
                changed |= ImGui.SliderFloat("Background strength", ref strength, 0, 1, "%.2f");
                var clean = RenderSettings.Create(false, true, 5, 8, true, start, end, strength);
                start = clean.BlurStart;
                end = clean.BlurEnd;
                strength = clean.Strength;
                ImGui.TextWrapped("Foreground blur is disabled. Increase strength or bring the blur distances closer for a stronger effect. Experimental: presets need visual calibration.");
                ImGui.PopID();
                return changed;
            }
            changed |= ImGui.Checkbox("Focus at camera look-at point (native)", ref cameraFocus);
            if (!cameraFocus) changed |= ImGui.SliderFloat("Focus distance", ref distance, 0.5f, 500, "%.2f");
            changed |= ImGui.SliderFloat("Aperture (lower = more blur)", ref aperture, 1.4f, 32, "f/%.1f");
            ImGui.PopID();
            return changed;
        }
    }
}
