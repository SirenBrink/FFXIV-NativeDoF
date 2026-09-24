using System.Runtime.InteropServices;

namespace NativeDof;

// Layout checked against ffxiv_dx11.exe SHA256 5BBC501D...4DFACC44.
// Field names cross-checked with aers/FFXIVClientStructs, commit
// 939aa1e1091ec069842ee518d50b6ebd1cabba74 (PostEffectManager).
[StructLayout(LayoutKind.Explicit, Size = 0x34)]
internal struct DofParameters
{
    [FieldOffset(0x00)] public byte Updated;
    [FieldOffset(0x01)] public byte ManualCurve;
    [FieldOffset(0x02)] public byte OverrideFocus;
    [FieldOffset(0x04)] public float NearFocus;
    [FieldOffset(0x08)] public float FarFocus;
    [FieldOffset(0x0C)] public float FarBlur;
    [FieldOffset(0x10)] public float NearBlurRate;
    [FieldOffset(0x14)] public float FarBlurRate;
    [FieldOffset(0x1C)] public float MaskRate;
    [FieldOffset(0x20)] public float FocusDistance;
    [FieldOffset(0x24)] public float FNumber;
    [FieldOffset(0x28)] public float CocDivisor;
    [FieldOffset(0x2C)] public float PreviousFrameWeight;
    [FieldOffset(0x30)] public float PreviousRangeScale;
}

internal sealed record RenderSettings(bool Apply, bool CameraFocus, float Distance, float Aperture)
{
    public static readonly RenderSettings Off = new(false, true, 5, 8);
    public static RenderSettings Create(bool apply, bool cameraFocus, float distance, float aperture) =>
        new(apply, cameraFocus, FiniteClamp(distance, 0.5f, 500, 5), FiniteClamp(aperture, 1.4f, 32, 8));
    private static float FiniteClamp(float x, float min, float max, float fallback) =>
        float.IsFinite(x) ? Math.Clamp(x, min, max) : fallback;
}

internal static unsafe class DofMemory
{
    public const int FlagsOffset = 0x4470;
    public const int ParametersOffset = 0x46E8;
    public const uint DofBit = 1u << 7;

    public static bool Ready(nint manager)
    {
        if (manager == 0) return false;
        var p = (byte*)manager;
        // Native setup and rendering both require all effects initialized.
        if (*(ulong*)(p + 0x4410) != ulong.MaxValue) return false;
        if (*(nint*)(p + 0x4010) == 0 || *(nint*)(p + 0x4030) == 0) return false;
        if (!ChainReady(p, 0x4C0) || !ChainReady(p, 0x4F8)) return false;
        var values = (DofParameters*)(p + ParametersOffset);
        return float.IsFinite(values->CocDivisor) && values->CocDivisor > 0
            && values->Updated <= 1 && values->ManualCurve <= 1 && values->OverrideFocus <= 1;
    }

    private static bool ChainReady(byte* manager, int offset) =>
        *(nint*)(manager + offset + 8) != 0
        && *(uint*)(manager + offset + 0x10) is > 0 and <= 32;
}

// Scope is entirely inside one native render call, on that call's thread.
// Only fields owned by the override are restored; other effects retain native changes.
internal unsafe ref struct DofOverride
{
    private readonly byte* manager;
    private readonly uint originalDofBit;
    private readonly DofParameters saved;

    public DofOverride(nint address, RenderSettings settings)
    {
        manager = (byte*)address;
        var flags = (uint*)(manager + DofMemory.FlagsOffset);
        var parameters = (DofParameters*)(manager + DofMemory.ParametersOffset);
        originalDofBit = *flags & DofMemory.DofBit;
        saved = *parameters;
        parameters->Updated = 1;
        parameters->ManualCurve = 0;
        parameters->OverrideFocus = settings.CameraFocus ? (byte)0 : (byte)1;
        parameters->FocusDistance = settings.Distance;
        parameters->FNumber = settings.Aperture;
        *flags |= DofMemory.DofBit;
    }

    public void Dispose()
    {
        var parameters = (DofParameters*)(manager + DofMemory.ParametersOffset);
        parameters->Updated = saved.Updated;
        parameters->ManualCurve = saved.ManualCurve;
        parameters->OverrideFocus = saved.OverrideFocus;
        parameters->FocusDistance = saved.FocusDistance;
        parameters->FNumber = saved.FNumber;
        var flags = (uint*)(manager + DofMemory.FlagsOffset);
        *flags = (*flags & ~DofMemory.DofBit) | originalDofBit;
    }
}
