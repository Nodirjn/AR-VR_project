using UnityEngine;

/// <summary>
/// Procedural car sound, synthesised on the audio thread so no recordings are needed. The engine is modelled, not
/// drawn as sine waves: every cylinder fires a pressure pulse (its own strength, plus combustion-to-combustion
/// jitter) into an exhaust made of two pipe resonances (feedback comb filters) and the silencer's body and rasp
/// resonances, so the note comes from the firing order and the plumbing, as in a real car: the E30's four-cylinder
/// rasp, the M4's inline-six howl with turbo whistle, blow-off and overrun crackle on a lift. Also an electric
/// motor's whine, intake, road and wind noise, tyre squeal, the horn and one-shots (starter, doors, shifts, clicks).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class TestDriveAudio : MonoBehaviour
{
    // Written on the main thread, read on the audio thread.
    private volatile bool running, electric, horn;
    private volatile float rpm, load, speed, slip, idleRpm = 800f, redlineRpm = 7000f;
    private volatile int cylinders = 6;
    private volatile bool turbo;
    private volatile int startCue, stopCue, doorOpenCue, doorCloseCue, shiftCue, clickCue;
    private volatile float master = 1f;

    private int startSeen, stopSeen, doorOpenSeen, doorCloseSeen, shiftSeen, clickSeen;
    private double sampleRate = 48000.0;
    private double motorPhase, hornPhaseA, hornPhaseB;
    private float rpmSmooth, loadSmooth, speedSmooth, slipSmooth, hornEnv;
    private float starterT = -1f, stopT = -1f, doorOpenT = -1f, doorCloseT = -1f, shiftT = -1f, clickT = -1f;
    private float bandSqueal1, bandSqueal2, lowWind, lowRoad, lowOut;
    private uint rng = 2463534242u;

    // Engine model state (audio thread).
    private double cyclePhase;                 // 0..1 over one engine cycle (two crank turns)
    private int nextFiring;
    private float dcIn, dcOut;
    private float pulseEnv, pulseAmp, crackleEnv, blowOffT = -1f, boost, lastLoad, lowCrackle, lowIntake, lowRumble;
    private readonly float[] cylinderTrim = { 1f, 0.9f, 1.07f, 0.95f, 1.04f, 0.92f, 1.02f, 0.97f };
    private readonly float[] pipeA = new float[4096];
    private readonly float[] pipeB = new float[4096];
    private int pipeIndex;
    private float pipeLowA, pipeLowB;
    private readonly Biquad body = new Biquad(), rasp = new Biquad(), honk = new Biquad(), whistle = new Biquad(), hiss = new Biquad();
    private double turboPhase;

    private sealed class Biquad
    {
        private float b0, b1, b2, a1, a2, z1, z2;

        public void Bandpass(float frequency, float q, double rate)
        {
            float w = 2f * Mathf.PI * Mathf.Clamp(frequency, 10f, (float)rate * 0.45f) / (float)rate;
            float alpha = Mathf.Sin(w) / (2f * q), cos = Mathf.Cos(w), a0 = 1f + alpha;
            b0 = alpha / a0; b1 = 0f; b2 = -alpha / a0;
            a1 = -2f * cos / a0; a2 = (1f - alpha) / a0;
        }

        public float Process(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }
    private float pendingDoorClose = -1f;
    private bool started;

    private void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;
        AudioSource source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0.65f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 4f;
        source.maxDistance = 120f;
        source.volume = 0.9f;
        // OnAudioFilterRead needs a playing source; a short silent loop keeps it running.
        AudioClip silence = AudioClip.Create("Test Drive Silence", 1024, 1, (int)sampleRate, false);
        silence.SetData(new float[1024], 0);
        source.clip = silence;
    }

    private void OnEnable()
    {
        AudioSource source = GetComponent<AudioSource>();
        if (source != null && !source.isPlaying) source.Play();
        started = true;
    }

    private void Update()
    {
        if (pendingDoorClose > 0f)
        {
            pendingDoorClose -= Time.deltaTime;
            if (pendingDoorClose <= 0f) doorCloseCue++;
        }
    }

    public void SetState(TestDriveCarProfile profile, bool isRunning, bool isStarting, float engineRpm, float throttle,
        float speedMetres, float wheelSlip, bool hornPressed)
    {
        running = isRunning;
        electric = profile.IsElectric;
        cylinders = Mathf.Max(2, profile.Cylinders);
        turbo = profile.Key == "M4" || profile.Key == "Concept";
        idleRpm = profile.IdleRpm;
        redlineRpm = profile.RedlineRpm;
        rpm = engineRpm;
        load = throttle;
        speed = speedMetres;
        slip = wheelSlip;
        horn = hornPressed;
    }

    public void SetMaster(float value) => master = Mathf.Clamp01(value);
    public void PlayStart(bool isElectric) => startCue++;
    public void PlayStop(bool isElectric) => stopCue++;
    public void PlayDoorOpen() => doorOpenCue++;
    public void PlayDoorClose() => doorCloseCue++;
    public void PlayDoorCloseSoon(float delay) => pendingDoorClose = delay;
    public void PlayShift() => shiftCue++;
    public void PlayClick() => clickCue++;

    private float Noise()
    {
        rng ^= rng << 13;
        rng ^= rng >> 17;
        rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 8388608f - 1f;
    }

    private static float OnePole(ref float state, float input, float cutoff, double rate)
    {
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / (float)rate);
        state += a * (input - state);
        return state;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!started) return;
        // Pick up one-shot cues.
        if (startSeen != startCue) { startSeen = startCue; starterT = 0f; }
        if (stopSeen != stopCue) { stopSeen = stopCue; stopT = 0f; }
        if (doorOpenSeen != doorOpenCue) { doorOpenSeen = doorOpenCue; doorOpenT = 0f; }
        if (doorCloseSeen != doorCloseCue) { doorCloseSeen = doorCloseCue; doorCloseT = 0f; }
        if (shiftSeen != shiftCue) { shiftSeen = shiftCue; shiftT = 0f; if (load > 0.6f) crackleEnv = 0.7f; }
        if (clickSeen != clickCue) { clickSeen = clickCue; clickT = 0f; }

        double rate = sampleRate;
        float dt = (float)(1.0 / rate);
        int frames = data.Length / channels;
        float targetRpm = rpm, targetLoad = running ? load : 0f, targetSpeed = speed, targetSlip = slip;
        bool isElectric = electric, isRunning = running, hornOn = horn;
        int cyl = Mathf.Clamp(cylinders, 2, 8);
        bool hasTurbo = turbo;
        float idle = Mathf.Max(400f, idleRpm), redline = redlineRpm, gain = master;
        // Exhaust voicing per engine: a four's buzzier, higher rasp; a six's deeper body and smoother howl.
        float rpmNow = Mathf.Max(rpmSmooth, 1f), rpmFraction = Mathf.Clamp01(rpmNow / Mathf.Max(1f, redline));
        bool four = cyl <= 4;
        body.Bandpass(four ? 135f : 105f, 1.1f, rate);
        rasp.Bandpass((four ? 1350f : 950f) + 900f * rpmFraction, 1.6f, rate);
        honk.Bandpass((four ? 420f : 330f) + 160f * rpmFraction, 2.2f, rate);
        whistle.Bandpass(2600f + 5200f * boost, 9f, rate);
        hiss.Bandpass(3200f, 0.9f, rate);
        int pipeLengthA = Mathf.Clamp((int)(rate / (four ? 172.0 : 148.0)), 8, pipeA.Length - 1);
        int pipeLengthB = Mathf.Clamp((int)(rate / (four ? 263.0 : 221.0)), 8, pipeB.Length - 1);
        // A quick lift of the pedal at high boost opens the blow-off valve.
        if (hasTurbo && boost > 0.45f && lastLoad - targetLoad > 0.4f) blowOffT = 0f;
        lastLoad = targetLoad;

        for (int f = 0; f < frames; f++)
        {
            rpmSmooth += (targetRpm - rpmSmooth) * 0.0025f;
            loadSmooth += (targetLoad - loadSmooth) * 0.0015f;
            speedSmooth += (targetSpeed - speedSmooth) * 0.001f;
            slipSmooth += (targetSlip - slipSmooth) * 0.004f;
            float sample = 0f;

            if (isElectric)
            {
                // Motor whine: a pure tone at the inverter's switching harmonic plus a quieter octave.
                float motorHz = rpmSmooth / 60f * 3f;
                if (isRunning && motorHz > 5f)
                {
                    motorPhase += motorHz * dt;
                    if (motorPhase > 1.0) motorPhase -= 1.0;
                    float p = (float)motorPhase * 2f * Mathf.PI;
                    float level = 0.018f + 0.05f * loadSmooth;
                    sample += (Mathf.Sin(p) + 0.35f * Mathf.Sin(2.01f * p)) * level * Mathf.Clamp01(motorHz / 120f);
                }
            }
            else if (isRunning || stopT >= 0f)
            {
                float engineRpm = Mathf.Max(rpmSmooth, isRunning ? idle * 0.92f : 0f);
                float rpmFactor = Mathf.Clamp01(engineRpm / Mathf.Max(1f, redline));
                // Firing: cyl pulses evenly spaced over two crank turns, each with its cylinder's trim and jitter
                // (more at idle, where combustion is least even).
                cyclePhase += engineRpm / 120.0 * dt;
                if (cyclePhase >= 1.0) { cyclePhase -= 1.0; nextFiring = 0; }
                if (nextFiring < cyl && cyclePhase >= (double)nextFiring / cyl)
                {
                    float jitter = Noise() * Mathf.Lerp(0.16f, 0.05f, rpmFactor);
                    float strength = isRunning ? 0.4f + 0.6f * loadSmooth : 0.15f;
                    pulseAmp = strength * cylinderTrim[nextFiring % cylinderTrim.Length] * (1f + jitter);
                    pulseEnv = 1f;
                    // Overrun: unburnt fuel popping in the hot exhaust when the pedal is lifted at high revs.
                    if (isRunning && loadSmooth < 0.06f && engineRpm > 2800f && Noise() > 0.86f) crackleEnv = 1f;
                    nextFiring++;
                }
                // The pulse: a sharp pressure step that decays within about a millisecond and a half.
                float pulse = pulseAmp * pulseEnv;
                pulseEnv *= 0.9855f;
                float crackle = 0f;
                if (crackleEnv > 0.001f)
                {
                    crackle = OnePole(ref lowCrackle, Noise(), 1800f, rate) * crackleEnv * 1.6f;
                    crackleEnv *= 0.9965f;
                }
                float raw = pulse + crackle + Noise() * 0.015f * (0.3f + loadSmooth);
                // DC blocker: a pressure pulse is followed by its rarefaction, so the exhaust carries no offset.
                float excitation = raw - dcIn + 0.995f * dcOut;
                dcIn = raw;
                dcOut = excitation;

                // Exhaust pipes: two feedback combs (the pipes' standing waves), damped by a low-pass in the loop.
                int readA = (pipeIndex - pipeLengthA + pipeA.Length) % pipeA.Length;
                int readB = (pipeIndex - pipeLengthB + pipeB.Length) % pipeB.Length;
                float pipeOutA = OnePole(ref pipeLowA, pipeA[readA], 2400f, rate);
                float pipeOutB = OnePole(ref pipeLowB, pipeB[readB], 2800f, rate);
                pipeA[pipeIndex] = excitation + pipeOutA * 0.62f;
                pipeB[pipeIndex] = excitation + pipeOutB * 0.48f;
                pipeIndex = (pipeIndex + 1) % pipeA.Length;
                float pipes = pipeOutA * 0.7f + pipeOutB * 0.45f;

                // Silencer: body thump, mid honk and the metallic rasp that opens up with load and revs.
                float thump = body.Process(excitation) * 3.2f;
                float mid = honk.Process(pipes) * 1.5f;
                float edge = rasp.Process(pipes + excitation * 0.5f) * (0.5f + 1.6f * loadSmooth) * (0.4f + rpmFactor);
                float rumble = OnePole(ref lowRumble, excitation, 70f, rate) * 2.4f;
                float engine = (thump + pipes * 0.9f + mid + edge + rumble) * 0.45f;

                // Intake roar: noise breathing at the firing rate.
                float breathing = 0.5f + 0.5f * Mathf.Sin((float)(cyclePhase * cyl) * 2f * Mathf.PI);
                engine += OnePole(ref lowIntake, Noise(), 700f + 2200f * rpmFactor, rate) * breathing * loadSmooth * rpmFactor * 0.35f;

                // Turbo: boost follows load and revs with spool lag; whistle rises with it, blow-off hiss on a lift.
                if (hasTurbo)
                {
                    float wantBoost = isRunning ? Mathf.Clamp01(loadSmooth * Mathf.InverseLerp(1500f, 4500f, engineRpm)) : 0f;
                    boost += (wantBoost - boost) * (wantBoost > boost ? 0.00004f : 0.0003f);
                    turboPhase += (2600.0 + 5200.0 * boost) * dt;
                    if (turboPhase > 1.0) turboPhase -= 1.0;
                    engine += (Mathf.Sin((float)turboPhase * 2f * Mathf.PI) * 0.6f + whistle.Process(Noise())) * 0.035f * boost * boost;
                    if (blowOffT >= 0f)
                    {
                        engine += hiss.Process(Noise()) * 0.35f * Mathf.Exp(-blowOffT * 7f) * Mathf.Clamp01(blowOffT * 60f);
                        blowOffT += dt;
                        if (blowOffT > 0.7f) blowOffT = -1f;
                    }
                }

                // Soft saturation, as a loud exhaust drives the cabin's panels.
                float drive = 1.2f + 1.8f * loadSmooth;
                float shaped = (float)System.Math.Tanh(engine * drive) / drive;
                float level = (0.32f + 0.22f * rpmFactor + 0.12f * loadSmooth) * 1.4f;
                if (stopT >= 0f && !isRunning) level *= Mathf.Clamp01(1f - stopT / 0.8f);
                sample += shaped * level;
            }

            // Starter motor: a whirring rise, then the catch.
            if (starterT >= 0f)
            {
                float t = starterT;
                if (isElectric)
                {
                    float chime = t < 0.45f ? Mathf.Sin(2f * Mathf.PI * (t < 0.18f ? 880f : 1320f) * t) * Mathf.Exp(-t * 6f) * 0.12f : 0f;
                    sample += chime;
                }
                else if (t < 0.95f)
                {
                    float crank = 12f + 3f * Mathf.Sin(t * 40f);
                    float whir = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.04f + Mathf.Sin(2f * Mathf.PI * crank * t) * 0.09f;
                    sample += whir * Mathf.Clamp01(t * 8f);
                }
                starterT += dt;
                if (starterT > 1.3f) starterT = -1f;
            }
            if (stopT >= 0f) { stopT += dt; if (stopT > 1f) stopT = -1f; }

            // Road and tyre noise grows with speed; squeal with slip.
            float roadLevel = Mathf.Clamp01(speedSmooth / 30f);
            sample += OnePole(ref lowRoad, Noise(), 180f + 260f * roadLevel, rate) * 0.09f * roadLevel;
            float windLevel = Mathf.Clamp01(speedSmooth / 45f);
            sample += OnePole(ref lowWind, Noise(), 500f + 900f * windLevel, rate) * 0.07f * windLevel * windLevel;
            float squeal = Mathf.Clamp01((slipSmooth - 0.32f) * 2.2f) * Mathf.Clamp01(speedSmooth / 3f);
            if (squeal > 0.001f)
            {
                float n = Noise();
                float b1 = OnePole(ref bandSqueal1, n, 1400f, rate);
                float b2 = OnePole(ref bandSqueal2, b1, 900f, rate);
                sample += (b1 - b2) * 0.9f * squeal;
            }

            // Horn: two detuned tones, a fifth apart, with a short attack.
            hornEnv += ((hornOn ? 1f : 0f) - hornEnv) * 0.004f;
            if (hornEnv > 0.001f)
            {
                hornPhaseA += 415.0 * dt; if (hornPhaseA > 1.0) hornPhaseA -= 1.0;
                hornPhaseB += 523.0 * dt; if (hornPhaseB > 1.0) hornPhaseB -= 1.0;
                float a = hornPhaseA < 0.5 ? 1f : -1f, b = hornPhaseB < 0.5 ? 1f : -1f;
                sample += (a + b) * 0.06f * hornEnv;
            }

            // Door: a latch click, then the hollow thump of the panel.
            if (doorOpenT >= 0f)
            {
                float t = doorOpenT;
                sample += Noise() * Mathf.Exp(-t * 90f) * 0.25f + Mathf.Sin(2f * Mathf.PI * 320f * t) * Mathf.Exp(-t * 40f) * 0.08f;
                doorOpenT += dt;
                if (doorOpenT > 0.4f) doorOpenT = -1f;
            }
            if (doorCloseT >= 0f)
            {
                float t = doorCloseT;
                sample += Mathf.Sin(2f * Mathf.PI * 62f * t) * Mathf.Exp(-t * 14f) * 0.45f + Noise() * Mathf.Exp(-t * 55f) * 0.18f;
                doorCloseT += dt;
                if (doorCloseT > 0.6f) doorCloseT = -1f;
            }
            if (shiftT >= 0f)
            {
                sample += Noise() * Mathf.Exp(-shiftT * 120f) * 0.05f;
                shiftT += dt;
                if (shiftT > 0.1f) shiftT = -1f;
            }
            if (clickT >= 0f)
            {
                sample += Mathf.Sin(2f * Mathf.PI * 1900f * clickT) * Mathf.Exp(-clickT * 160f) * 0.12f;
                clickT += dt;
                if (clickT > 0.06f) clickT = -1f;
            }

            sample = OnePole(ref lowOut, sample, 7000f, rate) * gain;
            sample = Mathf.Clamp(sample, -1f, 1f);
            int offset = f * channels;
            for (int c = 0; c < channels; c++) data[offset + c] += sample;
        }
    }
}
