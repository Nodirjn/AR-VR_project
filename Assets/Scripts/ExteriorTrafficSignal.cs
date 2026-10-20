using UnityEngine;

public enum ExteriorSignalPhase { EastWestGreen, EastWestAmber, NorthSouthGreen, NorthSouthAmber, Red }

/// <summary>Shared timed intersection controller used by signal heads and waypoint traffic.</summary>
[DisallowMultipleComponent]
public sealed class ExteriorTrafficSignal : MonoBehaviour
{
    [SerializeField, Min(1f)] private float greenSeconds = 18f;
    [SerializeField, Min(0.5f)] private float amberSeconds = 3f;
    [SerializeField, Min(0.5f)] private float redSeconds = 1f;
    [SerializeField] private float phaseOffset;
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
            float cycle = greenSeconds * 2f + amberSeconds * 2f + redSeconds;
            float t = Mathf.Repeat(Time.time + phaseOffset, cycle);
            if (t < greenSeconds) return ExteriorSignalPhase.EastWestGreen;
            if (t < greenSeconds + amberSeconds) return ExteriorSignalPhase.EastWestAmber;
            if (t < greenSeconds * 2f + amberSeconds) return ExteriorSignalPhase.NorthSouthGreen;
            if (t < greenSeconds * 2f + amberSeconds * 2f) return ExteriorSignalPhase.NorthSouthAmber;
            return ExteriorSignalPhase.Red;
        }
    }

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

    private void Update()
    {
        if (Time.time < refreshAt) return;
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
