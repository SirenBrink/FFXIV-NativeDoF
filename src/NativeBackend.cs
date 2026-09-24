using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace NativeDof;

internal sealed unsafe class NativeBackend : IDisposable
{
    public const string ExpectedHash = "5BBC501DD5C7F22FD61A11D08C25356041D878DB7CD83203ADAE393E4DFACC44";
    // The frame dispatcher calls native post-effect setup, then rendering.
    private const int RenderRva = 0x2BABD0;
    private const int SceneRva = 0x2BA1F0;
    private const int ManagerGlobalRva = 0x28F6498;
    private const int MainView = 0x1E;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RenderDelegate(nint renderer, byte drawScene, int view);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SceneDelegate(nint renderer);
    private Hook<RenderDelegate>? hook;
    private Hook<SceneDelegate>? sceneHook;
    private readonly nint managerGlobal;
    private readonly IPluginLog log;
    private RenderSettings settings = RenderSettings.Off;
    private long renderCalls;
    private long appliedCalls;
    private long worldSceneCalls;
    private long resourceSkips;
    private long sceneCalls;
    private long sceneOverrides;
    private int cocDisabled = -1;
    private float effectiveFocus;
    private float effectiveAperture;
    private uint flagsAtWorldEntry;
    private uint flagsAtSceneEnd;
    private int lastDrawScene;
    private int lastView = -1;
    private string status = "Starting";
    [ThreadStatic] private static bool insideOverride;
    public bool Available => hook != null && sceneHook != null;
    public float CameraDistance
    {
        get
        {
            if (!Available) return float.NaN;
            var manager = CameraManager.Instance();
            var camera = manager == null ? null : manager->Camera;
            return camera == null ? float.NaN : camera->Distance;
        }
    }
    public bool IsFirstPerson
    {
        get
        {
            if (!Available) return false;
            var manager = CameraManager.Instance();
            var camera = manager == null ? null : manager->Camera;
            return camera != null && camera->ZoomMode == CameraZoomMode.FirstPerson;
        }
    }
    public long RenderCalls => Interlocked.Read(ref renderCalls);
    public long AppliedCalls => Interlocked.Read(ref appliedCalls);
    public int LastView => Volatile.Read(ref lastView);
    public long WorldSceneCalls => Interlocked.Read(ref worldSceneCalls);
    public long ResourceSkips => Interlocked.Read(ref resourceSkips);
    public long SceneOverrides => Interlocked.Read(ref sceneOverrides);
    public int CocDisabled => Volatile.Read(ref cocDisabled);
    public float EffectiveFocus => Volatile.Read(ref effectiveFocus);
    public float EffectiveAperture => Volatile.Read(ref effectiveAperture);
    public string Status => Volatile.Read(ref status);

    public NativeBackend(IGameInteropProvider interop, ISigScanner scanner, IPluginLog pluginLog)
    {
        log = pluginLog;
        try
        {
            using var process = Process.GetCurrentProcess();
            var module = process.MainModule ?? throw new InvalidOperationException("No game module");
            using var file = File.OpenRead(module.FileName);
            if (Convert.ToHexString(SHA256.HashData(file)) != ExpectedHash)
                throw new InvalidOperationException("Game executable differs from the verified build; native access disabled.");
            var address = module.BaseAddress + RenderRva;
            // Dalamud's hook backend may retain a forwarding jump after unload.
            // Validate its pristine module snapshot, never the patched live entry.
            // HookFromAddress handles existing hook chains when reloading.
            if (!scanner.IsCopy || scanner.Module.BaseAddress != module.BaseAddress)
                throw new InvalidOperationException("Pristine game-module snapshot unavailable; native access disabled.");
            var originalEntry = scanner.SearchBase + RenderRva;
            byte[] expected = Convert.FromHexString("48895C240848896C241048897424185741544155415641574883EC20448B0D");
            if (!new ReadOnlySpan<byte>((void*)originalEntry, expected.Length).SequenceEqual(expected))
                throw new InvalidOperationException("Original render entry differs; native access disabled.");
            byte[] expectedScene = Convert.FromHexString("405357415441554883EC5865488B0425580000004C8BE98B15");
            if (!new ReadOnlySpan<byte>((void*)(scanner.SearchBase + SceneRva), expectedScene.Length).SequenceEqual(expectedScene))
                throw new InvalidOperationException("Original scene entry differs; native access disabled.");
            var managerSignature = scanner.SearchBase + 0x668C5;
            if (*(byte*)managerSignature != 0x48 || *(byte*)(managerSignature + 1) != 0x8B
                || *(byte*)(managerSignature + 2) != 0x0D
                || managerSignature + 7 + *(int*)(managerSignature + 3) != scanner.SearchBase + ManagerGlobalRva)
                throw new InvalidOperationException("DoF manager signature differs; native access disabled.");
            managerGlobal = module.BaseAddress + ManagerGlobalRva;
            hook = interop.HookFromAddress<RenderDelegate>(address, RenderDetour);
            sceneHook = interop.HookFromAddress<SceneDelegate>(module.BaseAddress + SceneRva, SceneDetour);
            hook.Enable();
            sceneHook.Enable();
            status = "Verified build; observing renders (effect off)";
            log.Information("Native DoF 0.1.6: verified scene and render entries; First-person suspension enabled.");
        }
        catch (Exception ex)
        {
            sceneHook?.Dispose();
            sceneHook = null;
            hook?.Dispose();
            hook = null;
            status = ex.Message;
            log.Error(ex, "Native DoF backend unavailable");
        }
    }

    public void Publish(RenderSettings value) => Volatile.Write(ref settings, value);

    public void ReportDiagnostics(string activity)
    {
        var current = Volatile.Read(ref settings);
        log.Information($"Native DoF diagnostics: activity={activity}; requested={current.Apply}; "
            + $"renders={RenderCalls}; worldScene={WorldSceneCalls}; applied={AppliedCalls}; "
            + $"resourceSkips={ResourceSkips}; lastView=0x{LastView:X}; drawScene={Volatile.Read(ref lastDrawScene)}; "
            + $"scenes={Interlocked.Read(ref sceneCalls)}; sceneOverrides={SceneOverrides}; "
            + $"worldFlags=0x{Volatile.Read(ref flagsAtWorldEntry):X}; endFlags=0x{Volatile.Read(ref flagsAtSceneEnd):X}; "
            + $"cocDisabled={CocDisabled}; effectiveFocus={EffectiveFocus:F3}; effectiveAperture={EffectiveAperture:F2}; "
            + $"cameraFocus={current.CameraFocus}; distance={current.Distance:F2}; aperture={current.Aperture:F2}; "
            + $"background={current.Background}; blurStart={current.BlurStart:F2}; blurEnd={current.BlurEnd:F2}; strength={current.Strength:F2}; status={Status}");
    }

    private void SceneDetour(nint renderer)
    {
        Interlocked.Increment(ref sceneCalls);
        var current = Volatile.Read(ref settings);
        // Native normal-scene branch guards; cover preparation, all world passes,
        // and final DoF-dependent submission before restoring the original state.
        if (!current.Apply || insideOverride || IsFirstPerson || *(byte*)(renderer + 0x38358) != 0
            || *(int*)(renderer + 0x3834C) != -1)
        {
            sceneHook!.Original(renderer);
            return;
        }
        var manager = *(nint*)managerGlobal;
        if (!DofMemory.Ready(manager))
        {
            Interlocked.Increment(ref resourceSkips);
            Volatile.Write(ref status, "Waiting for native DoF resources");
            sceneHook!.Original(renderer);
            return;
        }
        insideOverride = true;
        try
        {
            using var scope = new DofOverride(manager, current);
            sceneHook!.Original(renderer);
            Volatile.Write(ref flagsAtSceneEnd, *(uint*)(manager + DofMemory.FlagsOffset));
            Interlocked.Increment(ref sceneOverrides);
            Volatile.Write(ref status, "Full-scene native DoF override applied; visual verification required");
        }
        finally { insideOverride = false; }
    }

    private void RenderDetour(nint renderer, byte drawScene, int view)
    {
        Interlocked.Increment(ref renderCalls);
        Volatile.Write(ref lastView, view);
        Volatile.Write(ref lastDrawScene, drawScene);
        if (drawScene != 0 && view == MainView) Interlocked.Increment(ref worldSceneCalls);
        if (!insideOverride || drawScene == 0 || view != MainView)
        {
            hook!.Original(renderer, drawScene, view);
            return;
        }
        var manager = *(nint*)managerGlobal;
        Volatile.Write(ref flagsAtWorldEntry, *(uint*)(manager + DofMemory.FlagsOffset));
        hook!.Original(renderer, drawScene, view);
        Interlocked.Increment(ref appliedCalls);
        // Observe the CoC consumer, not just the manager values we supplied.
        var parts = *(nint*)(manager + 0x4C8);
        var coc = parts == 0 ? 0 : *(nint*)parts;
        if (coc == 0) return;
        Volatile.Write(ref cocDisabled, *(byte*)(coc + 0xF6));
        Volatile.Write(ref effectiveFocus, *(float*)(coc + 0xE0));
        Volatile.Write(ref effectiveAperture, *(float*)(coc + 0xE4));
    }

    public void Dispose()
    {
        Publish(RenderSettings.Off);
        sceneHook?.Disable();
        hook?.Disable();
        sceneHook?.Dispose();
        sceneHook = null;
        hook?.Dispose();
        hook = null;
    }
}
