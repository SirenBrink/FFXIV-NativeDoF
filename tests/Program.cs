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
        var close = new CloseFocusPolicy();
        var backgroundProfile = RenderSettings.Create(true, false, 7, 8, true, 15, 60, 0.35f);
        Check(close.Select(backgroundProfile, true, 3, 2, 1.4f) == backgroundProfile, "Zoomed out preserves profile");
        var closeResult = close.Select(backgroundProfile, true, 2, 2, 1.4f);
        Check(close.Active && closeResult.CameraFocus && !closeResult.Background && closeResult.Aperture == 1.4f,
            "Close zoom replaces curve with strong native look-at focus");
        Check(close.Select(backgroundProfile, true, 2.4f, 2, 1.4f).CameraFocus && close.Active, "Close threshold hysteresis");
        Check(close.Select(backgroundProfile, true, 2.6f, 2, 1.4f) == backgroundProfile && !close.Active, "Zoom out restores original profile");
        Check(close.Select(backgroundProfile, false, 1, 2, 1.4f) == backgroundProfile, "Unchecked override preserves profile");
        close.Select(backgroundProfile, true, 1, 2, 1.4f);
        Check(close.Select(RenderSettings.Off, true, 1, 2, 1.4f) == RenderSettings.Off && !close.Active, "Suspension overrides close focus");
        Check(close.Select(backgroundProfile, true, float.NaN, 2, 1.4f) == backgroundProfile, "Missing camera preserves profile");
        Check(close.Select(backgroundProfile, true, 0, 2, 1.4f) == backgroundProfile, "Invalid camera distance rejected");
        var policy = new ActivationPolicy();
        var eligible = new ActivationContext(true, true, false, false, false, false, false);
        Check(policy.Evaluate(eligible, true, false, false, 2, 10) == "Waiting: activation delay", "Enable starts delay");
        Check(policy.Evaluate(eligible, true, false, false, 2, 11.9) != ActivationPolicy.Active, "Delay not shortened by updates");
        Check(policy.Evaluate(eligible, true, false, false, 2, 12) == ActivationPolicy.Active, "Delay expires at boundary");
        Check(policy.Evaluate(eligible with { Combat = true }, true, false, false, 2, 13) == "Suspended: combat", "Combat suspends immediately");
        Check(policy.Evaluate(eligible, true, false, false, 2, 14) != ActivationPolicy.Active, "Combat exit restarts delay");
        Check(policy.Evaluate(eligible with { Combat = true }, false, false, false, 2, 16) == ActivationPolicy.Active, "Enabled combat profile does not suspend");
        Check(policy.Evaluate(eligible with { Duty = true }, false, true, false, 0, 17) == "Suspended: duty", "Duty rule overrides combat permission");
        Check(policy.Evaluate(eligible with { Mounted = true }, false, false, true, 0, 18) == "Suspended: mounted", "Mount suspension");
        Check(policy.Evaluate(eligible with { FirstPerson = true }, false, false, false, 0, 19) == "Suspended: first-person camera", "First person always suspends");
        Check(policy.Evaluate(eligible with { GameBlocked = true }, false, false, false, 0, 20).StartsWith("Suspended: cutscene"), "Game-owned scenes always suspend");
        Check(policy.Evaluate(eligible with { Enabled = false }, false, false, false, 0, 21) == "Off", "Manual off overrides all rules");
        Check(policy.Evaluate(eligible with { Available = false }, false, false, false, 0, 22) == "Unavailable", "Unsupported backend cannot activate");
        Check(policy.Evaluate(eligible, true, true, true, float.NaN, 23) == ActivationPolicy.Active, "Invalid delay repaired");
        Check(ActivationPolicy.CleanDelay(-1) == 0 && ActivationPolicy.CleanDelay(999) == 30, "Delay bounded");
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
            using var scope = new DofOverride((nint)manager, RenderSettings.Create(true, true, 5, 8, true, 15, 60, 0.35f));
            Check(parameters->ManualCurve == 1 && parameters->NearBlurRate == 0, "Background curve disables foreground blur");
            Check(parameters->FarFocus == 15 && parameters->FarBlur == 60 && parameters->FarBlurRate == 0.35f,
                "Background range and strength applied independently of aperture");
            throw new InvalidOperationException("Simulated curve render failure");
        }
        catch (InvalidOperationException) { }
        Check(new ReadOnlySpan<byte>(manager, 0x48C0).SequenceEqual(baseline), "Curve fields restored on exception");
        var curve = RenderSettings.Create(true, true, 5, 8, true, 499, float.NaN, float.PositiveInfinity);
        Check(curve.BlurEnd > curve.BlurStart && curve.Strength == 0.15f, "Non-finite curve inputs repaired without singular range");
        curve = RenderSettings.Create(true, true, 5, 8, true, 30, 10, 2);
        Check(curve.BlurEnd == 30.5f && curve.Strength == 1, "Reversed curve range and excessive strength bounded");
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
        Console.WriteLine($"PASS: {checks} checks (activation transitions, layout, restoration, exception path, input validation, resource gating).");
    }
}
