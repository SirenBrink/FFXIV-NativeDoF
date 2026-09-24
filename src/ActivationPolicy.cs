namespace NativeDof;

internal readonly record struct ActivationContext(bool Available, bool Enabled, bool GameBlocked,
    bool FirstPerson, bool Combat, bool Duty, bool Mounted);

// Uses monotonic seconds supplied by the caller; no native memory or game services.
internal sealed class ActivationPolicy
{
    private double? eligibleSince;

    public static float CleanDelay(float delay) => float.IsFinite(delay) ? Math.Clamp(delay, 0, 30) : 0;

    public string Evaluate(ActivationContext context, bool suspendCombat, bool suspendDuty,
        bool suspendMounted, float delay, double now)
    {
        var reason = !context.Available ? "Unavailable" : !context.Enabled ? "Off"
            : context.GameBlocked ? "Suspended: cutscene, GPose, loading or logged out"
            : context.FirstPerson ? "Suspended: first-person camera"
            : suspendCombat && context.Combat ? "Suspended: combat"
            : suspendDuty && context.Duty ? "Suspended: duty"
            : suspendMounted && context.Mounted ? "Suspended: mounted"
            : null;
        if (reason != null)
        {
            eligibleSince = null;
            return reason;
        }
        eligibleSince ??= now;
        return now - eligibleSince.Value < CleanDelay(delay)
            ? "Waiting: activation delay" : Active;
    }

    public const string Active = "Enabled for gameplay";
}
