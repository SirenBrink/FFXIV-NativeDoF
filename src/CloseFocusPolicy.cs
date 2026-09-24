namespace NativeDof;

internal sealed class CloseFocusPolicy
{
    public bool Active { get; private set; }
    public static float CleanDistance(float value) => float.IsFinite(value) ? Math.Clamp(value, 0.5f, 10) : 2;

    public RenderSettings Select(RenderSettings profile, bool enabled, float cameraDistance, float threshold, float aperture)
    {
        threshold = CleanDistance(threshold);
        if (!profile.Apply || !enabled || !float.IsFinite(cameraDistance) || cameraDistance <= 0)
            Active = false;
        else
            Active = cameraDistance <= threshold + (Active ? 0.5f : 0);

        return Active ? RenderSettings.Create(true, true, profile.Distance, aperture) : profile;
    }
}
