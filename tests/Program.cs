using System.Runtime.InteropServices;
using NativeDof;

unsafe class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    static void Main()
    {
        Check(Marshal.SizeOf<DofParameters>() == 0x34, "Native parameter size");
        Check((int)Marshal.OffsetOf<DofParameters>(nameof(DofParameters.FocusDistance)) == 0x20, "Focus offset");
        Check((int)Marshal.OffsetOf<DofParameters>(nameof(DofParameters.FNumber)) == 0x24, "Aperture offset");
        byte* manager = stackalloc byte[0x48C0];
        new Span<byte>(manager, 0x48C0).Fill(0xA5);
        var flags = (uint*)(manager + DofMemory.FlagsOffset);
        var parameters = (DofParameters*)(manager + DofMemory.ParametersOffset);
        *parameters = new DofParameters { Updated = 0, ManualCurve = 1, OverrideFocus = 0,
            FocusDistance = 12, FNumber = 8, CocDivisor = 33, MaskRate = 0.5f };
        *flags = 0x4000;
        var baseline = new ReadOnlySpan<byte>(manager, 0x48C0).ToArray();
        using (var scope = new DofOverride((nint)manager, RenderSettings.Create(true, false, 6, 2.8f)))
        {
            Check(*flags == 0x4080, "Only DoF flag enabled");
            Check(parameters->Updated == 1 && parameters->ManualCurve == 0 && parameters->OverrideFocus == 1,
                "Physical DoF with manual focus");
            Check(parameters->FocusDistance == 6 && parameters->FNumber == 2.8f, "Requested settings applied");
            Check(parameters->CocDivisor == 33 && parameters->MaskRate == 0.5f, "Native tuning preserved");
        }
        Check(new ReadOnlySpan<byte>(manager, 0x48C0).SequenceEqual(baseline), "Exact restoration and neighboring memory preserved");
        try
        {
            using var scope = new DofOverride((nint)manager, RenderSettings.Create(true, true, 6, 2.8f));
            Check(parameters->OverrideFocus == 0, "Native camera focus selected");
            *flags |= 0x10000;
            parameters->PreviousFrameWeight = 0.9f;
            throw new InvalidOperationException("Simulated render exception");
        }
        catch (InvalidOperationException) { }
        Check(*flags == 0x14000, "Restoration preserves other effects' flag changes");
        Check(parameters->FNumber == 8 && parameters->FocusDistance == 12, "Exception restores owned parameters");
        Check(parameters->PreviousFrameWeight == 0.9f, "Unowned parameter changes retained");
        *flags |= DofMemory.DofBit;
        using (var scope = new DofOverride((nint)manager, RenderSettings.Off)) { }
        Check((*flags & DofMemory.DofBit) != 0, "Originally enabled DoF remains enabled");
        var clamped = RenderSettings.Create(true, false, float.NaN, float.PositiveInfinity);
        Check(clamped.Distance == 5 && clamped.Aperture == 8, "Non-finite inputs repaired");
        clamped = RenderSettings.Create(true, false, -20, 999);
        Check(clamped.Distance == 0.5f && clamped.Aperture == 32, "Inputs bounded");
        Check(!DofMemory.Ready(0), "Null manager rejected");
        new Span<byte>(manager, 0x48C0).Clear();
        Check(!DofMemory.Ready((nint)manager), "Uninitialized resources rejected");
        *(ulong*)(manager + 0x4410) = ulong.MaxValue;
        *(nint*)(manager + 0x4010) = 1;
        *(nint*)(manager + 0x4030) = 1;
        *(nint*)(manager + 0x4C8) = 1;
        *(uint*)(manager + 0x4D0) = 1;
        *(nint*)(manager + 0x500) = 1;
        *(uint*)(manager + 0x508) = 1;
        parameters->CocDivisor = 33;
        Check(DofMemory.Ready((nint)manager), "Ready resources accepted");
        *(uint*)(manager + 0x508) = 33;
        Check(!DofMemory.Ready((nint)manager), "Invalid chain rejected");
        Console.WriteLine($"PASS: {checks} checks (layout, restoration, exception path, input validation, resource gating).");
    }
}
