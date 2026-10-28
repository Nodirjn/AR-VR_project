using UnityEngine;

public enum ExteriorSignalPhase { EastWestGreen, EastWestAmber, NorthSouthGreen, NorthSouthAmber, Red }

/// <summary>Shared timed intersection controller used by signal heads and waypoint traffic.</summary>
[DisallowMultipleComponent]
public sealed class ExteriorTrafficSignal : MonoBehaviour
{
    /// <summary>How far past a stop line a mover still counts as waiting at it rather than committed to the junction.</summary>
    internal const float StopLineTolerance = 1f;
    [SerializeField, Min(1f)] private float greenSeconds = 18f;
    [SerializeField, Min(0.5f)] private float amberSeconds = 3f;
    [SerializeField, Min(0.5f)] private float redSeconds = 1f;
    [SerializeField] private float phaseOffset;
    [SerializeField, Min(0f)] private float stopLineDistance = 9f;
    [SerializeField] private Renderer[] eastWestRed, eastWestAmber, eastWestGreen;
    [SerializeField] private Renderer[] northSouthRed, northSouthAmber, northSouthGreen;
    [SerializeField] private Material redOn, redOff, amberOn, amberOff, greenOn, greenOff;
    [SerializeField] private Renderer walkLamp, waitLamp;
    [SerializeField] private Material walkOn, walkOff, waitOn, waitOff;
    private float refreshAt;
    private ExteriorSignalPhase cachedPhase;

    public ExteriorSignalPhase Phase
    {
        get
        {
            float t = Mathf.Repeat(Time.time + phaseOffset, CycleSeconds);
            if (t < greenSeconds) return ExteriorSignalPhase.EastWestGreen;
            if (t < greenSeconds + amberSeconds) return ExteriorSignalPhase.EastWestAmber;
            if (t < greenSeconds * 2f + amberSeconds) return ExteriorSignalPhase.NorthSouthGreen;
            if (t < greenSeconds * 2f + amberSeconds * 2f) return ExteriorSignalPhase.NorthSouthAmber;
            return ExteriorSignalPhase.Red;
        }
    }

    /// <summary>Distance from the junction centre to the stop line on every approach (9 m matches the painted lines).</summary>
    public float StopLineDistance => stopLineDistance;

    private float CycleSeconds => greenSeconds * 2f + amberSeconds * 2f + redSeconds;

    public bool AllowsVehicle(Vector3 travelDirection)
    {
        Vector3 d = Vector3.ProjectOnPlane(travelDirection, Vector3.up);
        ExteriorSignalPhase phase = Phase;
        return Mathf.Abs(d.x) >= Mathf.Abs(d.z)
            ? phase == ExteriorSignalPhase.EastWestGreen
            : phase == ExteriorSignalPhase.NorthSouthGreen;
    }

    /// <summary>Walk is permitted while vehicles travelling across the crossing are stopped.</summary>
    public bool AllowsPedestrian(Vector3 crossingDirection)
    {
        Vector3 d = Vector3.ProjectOnPlane(crossingDirection, Vector3.up);
        ExteriorSignalPhase phase = Phase;
        return Mathf.Abs(d.z) >= Mathf.Abs(d.x)
            ? phase == ExteriorSignalPhase.NorthSouthGreen || phase == ExteriorSignalPhase.NorthSouthAmber || phase == ExteriorSignalPhase.Red
            : phase == ExteriorSignalPhase.EastWestGreen || phase == ExteriorSignalPhase.EastWestAmber || phase == ExteriorSignalPhase.Red;
    }

    /// <summary>True when walking is permitted and enough of the walk window remains to cross in <paramref name="secondsNeeded"/>
    /// (capped at three quarters of the window, so slow walkers still get a turn).</summary>
    public bool PedestrianMayStart(Vector3 crossingDirection, float secondsNeeded)
    {
        if (!AllowsPedestrian(crossingDirection)) return false;
        Vector3 d = Vector3.ProjectOnPlane(crossingDirection, Vector3.up);
        float cycle = CycleSeconds;
        float t = Mathf.Repeat(Time.time + phaseOffset, cycle);
        // The walk window ends at the cycle end (north-south walkers) or when east-west amber ends (east-west walkers).
        float secondsLeft = (Mathf.Abs(d.z) >= Mathf.Abs(d.x) ? cycle : greenSeconds + amberSeconds) - t;
        if (secondsLeft < 0f) secondsLeft += cycle;
        return secondsLeft >= Mathf.Min(secondsNeeded, (greenSeconds + amberSeconds + redSeconds) * 0.75f);
    }

    /// <summary>Nearest signal whose stop line a mover heading along <paramref name="heading"/> has not passed yet.
    /// <paramref name="distanceToStopLine"/> is measured from the mover's front, which lies <paramref name="frontOffset"/> ahead of <paramref name="position"/>.</summary>
    internal static ExteriorTrafficSignal NextOnApproach(ExteriorTrafficSignal[] signals, Vector3 position, Vector3 heading,
        float frontOffset, float range, float lateralLimit, out float distanceToStopLine)
    {
        distanceToStopLine = float.PositiveInfinity;
        ExteriorTrafficSignal nearest = null;
        if (signals == null) return null;
        for (int i = 0; i < signals.Length; i++)
        {
            ExteriorTrafficSignal signal = signals[i];
            if (signal == null) continue;
            Vector3 offset = Vector3.ProjectOnPlane(signal.transform.position - position, Vector3.up);
            if (Mathf.Abs(offset.x * heading.z - offset.z * heading.x) > lateralLimit) continue;
            float toStopLine = Vector3.Dot(offset, heading) - signal.stopLineDistance - frontOffset;
            if (toStopLine < -StopLineTolerance || toStopLine > range || toStopLine >= distanceToStopLine) continue;
            distanceToStopLine = toStopLine;
            nearest = signal;
        }
        return nearest;
    }

    public void Configure(Renderer[] ewRed, Renderer[] ewAmber, Renderer[] ewGreen,
        Renderer[] nsRed, Renderer[] nsAmber, Renderer[] nsGreen,
        Material litRed, Material darkRed, Material litAmber, Material darkAmber,
        Material litGreen, Material darkGreen, float offset,
        Renderer walk = null, Renderer wait = null, Material litWalk = null, Material darkWalk = null,
        Material litWait = null, Material darkWait = null)
    {
        eastWestRed = ewRed; eastWestAmber = ewAmber; eastWestGreen = ewGreen;
        northSouthRed = nsRed; northSouthAmber = nsAmber; northSouthGreen = nsGreen;
        redOn = litRed; redOff = darkRed; amberOn = litAmber; amberOff = darkAmber;
        greenOn = litGreen; greenOff = darkGreen; phaseOffset = offset;
        walkLamp = walk; waitLamp = wait; walkOn = litWalk; walkOff = darkWalk;
        waitOn = litWait; waitOff = darkWait;
        cachedPhase = (ExteriorSignalPhase)(-1);
        refreshAt = 0f;
    }

    // The scene stores every lamp dark, so the current state is applied before the first frame renders.
    private void OnEnable()
    {
        cachedPhase = (ExteriorSignalPhase)(-1);
        RefreshLamps();
    }

    private void Update()
    {
        if (Time.time >= refreshAt) RefreshLamps();
    }

    private void RefreshLamps()
    {
        refreshAt = Time.time + 0.15f;
        ExteriorSignalPhase phase = Phase;
        if (phase == cachedPhase) return;
        cachedPhase = phase;
        bool ewGreenActive = phase == ExteriorSignalPhase.EastWestGreen;
        bool ewAmberActive = phase == ExteriorSignalPhase.EastWestAmber;
        bool nsGreenActive = phase == ExteriorSignalPhase.NorthSouthGreen;
        bool nsAmberActive = phase == ExteriorSignalPhase.NorthSouthAmber;
        SetLamps(eastWestRed, !(ewGreenActive || ewAmberActive), redOn, redOff);
        SetLamps(eastWestAmber, ewAmberActive, amberOn, amberOff);
        SetLamps(eastWestGreen, ewGreenActive, greenOn, greenOff);
        SetLamps(northSouthRed, !(nsGreenActive || nsAmberActive), redOn, redOff);
        SetLamps(northSouthAmber, nsAmberActive, amberOn, amberOff);
        SetLamps(northSouthGreen, nsGreenActive, greenOn, greenOff);
        bool walk = AllowsPedestrian(Vector3.forward);
        if (walkLamp != null && (walk ? walkOn : walkOff) != null) walkLamp.sharedMaterial = walk ? walkOn : walkOff;
        if (waitLamp != null && (walk ? waitOff : waitOn) != null) waitLamp.sharedMaterial = walk ? waitOff : waitOn;
    }

    private static void SetLamps(Renderer[] lamps, bool on, Material lit, Material unlit)
    {
        if (lamps == null) return;
        Material target = on ? lit : unlit;
        for (int i = 0; i < lamps.Length; i++)
            if (lamps[i] != null && target != null) lamps[i].sharedMaterial = target;
    }
}
