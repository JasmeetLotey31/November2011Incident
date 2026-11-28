using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes the game's station and weapon sounds in code (and from the CC0 recordings already in
/// Assets/Audio), so every sound is ours and fits the place:
/// - Station/: room tone (air, mains hum), hull groans, metal ticks, fluorescent hum and crackles,
///   the wave klaxon, the all-clear chime, radio squelch, the power going down, boots on deck plate;
/// - Guns/: a tighter pistol crack, the shotgun's boom with its pump, the energy guns' thump,
///   magazine out / in, slide release, an energy cell clicking in and charging.
/// Deterministic (seeded), 44.1 kHz mono. Menu: Tools / Audio / Rebuild Sounds.
/// </summary>
public static class SoundForge
{
    const int Rate = 44100;
    const string Station = "Assets/Audio/Station/";
    const string Guns = "Assets/Audio/Guns/";

    [MenuItem("Tools/Audio/Rebuild Sounds")]
    static void BuildMenu() => Debug.Log(BuildAll());

    public static string BuildAll()
    {
        foreach (string d in new[] { Station, Guns }) Directory.CreateDirectory(d);
        string log = "";
        // ---- the station ----
        log += Save(Station + "Room_Tone.wav", RoomTone(24f), true);
        for (int i = 0; i < 3; i++) log += Save(Station + $"Hull_Groan_{(char)('A' + i)}.wav", Groan(i), false);
        for (int i = 0; i < 4; i++) log += Save(Station + $"Metal_Tick_{(char)('A' + i)}.wav", Tick(i), false);
        log += Save(Station + "Light_Hum.wav", LightHum(), true);
        for (int i = 0; i < 3; i++) log += Save(Station + $"Light_Crackle_{(char)('A' + i)}.wav", Crackle(i), false);
        log += Save(Station + "Wave_Klaxon.wav", Klaxon(), false);
        log += Save(Station + "Wave_AllClear.wav", AllClear(), false);
        log += Save(Station + "Radio_In.wav", Radio(true), false);
        log += Save(Station + "Radio_Out.wav", Radio(false), false);
        log += Save(Station + "Power_Down.wav", PowerDown(), false);
        for (int i = 0; i < 6; i++) log += Save(Station + $"Step_Deck_{(char)('A' + i)}.wav", Step(i), false);
        // ---- the guns ----
        string[] shots = { "Assets/Audio/Pistol/Pistol_Fire_A.wav", "Assets/Audio/Pistol/Pistol_Fire_B.wav", "Assets/Audio/Pistol/Pistol_Fire_C.wav" };
        float[] pump = Pump();
        log += Save(Guns + "Shotgun_Pump.wav", pump, false);
        for (int i = 0; i < 3; i++)
        {
            float[] src = ReadWav(shots[i]);
            log += Save(Guns + $"Pistol_Fire_{(char)('A' + i)}.wav", PistolShot(src, i), false);
            log += Save(Guns + $"Shotgun_Fire_{(char)('A' + i)}.wav", ShotgunShot(src, pump, i), false);
            log += Save(Guns + $"Energy_Fire_{(char)('A' + i)}.wav", EnergyShot(i), false);
        }
        log += Save(Guns + "Mag_Out.wav", MagOut(), false);
        log += Save(Guns + "Mag_In.wav", MagIn(1), false);
        log += Save(Guns + "Slide_Release.wav", SlideRelease(), false);
        log += Save(Guns + "Cell_In.wav", CellIn(), false);
        return log;
    }

    // ================= station =================

    static float[] RoomTone(float seconds)
    {
        int n = (int)(seconds * Rate);
        var rng = new System.Random(1);
        var air = Lowpass(Brown(n, rng), 520f);
        var hiss = Bandpass(White(n, rng), 3200f, .7f);
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            float breathe = .8f + .2f * (float)Math.Sin(2 * Math.PI * .07 * t);           // the air handlers' slow swell
            float hum = .18f * S(50, t) + .09f * S(100, t, .3) + .05f * S(150, t) + .025f * S(200.4, t) + .05f * S(50.25, t, 1.1);
            o[i] = air[i] * .6f * breathe + hiss[i] * .05f * (.7f + .3f * (float)Math.Sin(2 * Math.PI * .13 * t + 1)) + hum * .35f;
        }
        return Loop(Normalize(o, .5f), 2f);
    }

    /// <summary>The hull flexing somewhere: low metal modes swelling and sagging in pitch, with stick-slip creaks.</summary>
    static float[] Groan(int v)
    {
        var rng = new System.Random(10 + v);
        float len = 3.2f + v * .6f;
        int n = (int)(len * Rate);
        float[][] modes = { new[] { 62f, 97f, 151f, 233f, 377f }, new[] { 71f, 119f, 167f, 260f }, new[] { 55f, 89f, 140f, 210f, 318f } };
        var exc = Lowpass(Brown(n, rng), 300f);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            exc[i] *= Mathf.Clamp01(t / 1f) * Mathf.Clamp01((len - t) / 1.5f);
        }
        // creaks: a train of tiny clicks at uneven gaps while it strains
        var creak = new float[n];
        for (float t = .5f; t < len - 1f; t += (float)(.02 + rng.NextDouble() * .05))
            creak[(int)(t * Rate)] = (float)(rng.NextDouble() * 2 - 1) * Mathf.Clamp01((t - .5f) / .8f);
        creak = Bandpass(creak, 1300f, 1.5f);
        var o = new float[n];
        foreach (float f in modes[v])
        {
            var m = GlidingResonator(exc, f, 1.4f, t => 1f - .06f * t);
            for (int i = 0; i < n; i++) o[i] += m[i] * (90f / f);
        }
        for (int i = 0; i < n; i++) o[i] = (float)Math.Tanh((o[i] + exc[i] * .15f + creak[i] * 2.5f) * 1.5f);
        return Normalize(o, .7f);
    }

    /// <summary>A sheet of metal ticking as it cools or takes weight: an impulse into a few high modes.</summary>
    static float[] Tick(int v)
    {
        var rng = new System.Random(20 + v);
        int n = (int)(.7f * Rate);
        var imp = Impulse(n, 0, 6, rng);
        var o = new float[n];
        float[] f = { 1300f + v * 410f, 2100f + v * 270f, 3400f - v * 190f };
        for (int k = 0; k < 3; k++)
        {
            var m = Mode(imp, f[k], .18f + .08f * k);
            for (int i = 0; i < n; i++) o[i] += m[i] / (k + 1);
        }
        var thunk = Mode(imp, 180f + v * 30f, .05f);
        for (int i = 0; i < n; i++) o[i] += thunk[i] * .4f;
        return Normalize(o, .6f);
    }

    static float[] LightHum()
    {
        int n = 3 * Rate;                                        // whole cycles of every partial: loops cleanly
        var rng = new System.Random(30);
        var noise = Bandpass(White(n, rng), 1200f, 1.2f);
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            o[i] = .5f * S(100, t) + .25f * S(200, t, .2) + .18f * S(300, t, .5) + .12f * S(400, t) + .08f * S(600, t, .9) + noise[i] * .03f;
        }
        return Normalize(o, .5f);
    }

    static float[] Crackle(int v)
    {
        var rng = new System.Random(40 + v);
        int n = (int)(.45f * Rate);
        var o = new float[n];
        int sparks = 3 + rng.Next(6);
        for (int s = 0; s < sparks; s++)
        {
            int at = rng.Next(n - Rate / 20);
            int len = (int)((.002 + rng.NextDouble() * .006) * Rate);
            for (int i = 0; i < len; i++) o[at + i] += (float)(rng.NextDouble() * 2 - 1);
            for (int i = 0; i < Rate * 3 / 100; i++) o[at + i] += .3f * S(100, (double)i / Rate) * (1f - i / (Rate * .03f));
        }
        return Normalize(Highpass(o, 900f), .7f);
    }

    /// <summary>The station alarm through a tinny ceiling speaker: two rising whoops.</summary>
    static float[] Klaxon()
    {
        int whoop = (int)(1.1f * Rate), gap = (int)(.2f * Rate);
        var o = new float[whoop * 2 + gap];
        for (int w = 0; w < 2; w++)
        {
            double phase = 0;
            for (int i = 0; i < whoop; i++)
            {
                float t = (float)i / whoop;
                double f = 380 + 520 * Math.Pow(t, .7);
                phase += 2 * Math.PI * f / Rate;
                double saw = 0;
                for (int h = 1; h <= 8; h++) saw += Math.Sin(phase * h) / h;
                float env = Mathf.Clamp01(i / (.04f * Rate)) * Mathf.Clamp01((whoop - i) / (.12f * Rate));
                o[w * (whoop + gap) + i] = (float)saw * env;
            }
        }
        o = Bandpass(o, 900f, .8f);
        for (int i = 0; i < o.Length; i++) o[i] = (float)Math.Tanh(o[i] * 2.5f);
        return Normalize(Speaker(o), .8f);
    }

    static float[] AllClear()
    {
        int n = (int)(1.2f * Rate);
        var o = new float[n];
        float[] notes = { 660f, 880f, 1320f };
        for (int k = 0; k < 3; k++)
        {
            int start = (int)(k * .22f * Rate);
            for (int i = 0; i < (int)(.5f * Rate) && start + i < n; i++)
            {
                double t = (double)i / Rate;
                float env = Mathf.Clamp01(i / (.005f * Rate)) * (float)Math.Exp(-t / .13);
                o[start + i] += (S(notes[k], t) + S(notes[k] * 3, t) / 9f) * env;
            }
        }
        return Normalize(Speaker(o), .6f);
    }

    static float[] Radio(bool opening)
    {
        var rng = new System.Random(opening ? 50 : 51);
        int n = (int)((opening ? .3f : .25f) * Rate);
        var noise = Bandpass(White(n, rng), 1800f, 1.2f);
        var o = new float[n];
        int click = opening ? 0 : n - Rate / 50;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float env = opening ? (float)Math.Exp(-t * 5) : Mathf.Clamp01(1f - t * 1.25f);
            o[i] = noise[i] * env * .8f;
        }
        for (int i = 0; i < Rate / 200; i++) o[Mathf.Min(n - 1, click + i)] += (i % 2 == 0 ? 1f : -1f) * (1f - i / (Rate / 200f));
        return Normalize(o, .5f);
    }

    /// <summary>The power dies: a heavy relay thunk, then the station's hum winding down to nothing.</summary>
    static float[] PowerDown()
    {
        var rng = new System.Random(60);
        int n = (int)(2.4f * Rate);
        var imp = Impulse(n, 0, 40, rng);
        var o = new float[n];
        foreach (var (f, d) in new[] { (60f, .35f), (120f, .25f), (410f, .08f) })
        {
            var m = Mode(imp, f, d);
            for (int i = 0; i < n; i++) o[i] += m[i] * (f < 200 ? 1.2f : .4f);
        }
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double f = 60 + 840 * Math.Exp(-t * 2.2);              // a whine falling away
            phase += 2 * Math.PI * f / Rate;
            float env = Mathf.Clamp01((float)t / .05f) * (float)Math.Exp(-t * 1.3);
            o[i] += (float)(Math.Sin(phase) + .4 * Math.Sin(phase * 2) + .2 * Math.Sin(phase * 3)) * env * .35f;
            o[i] += .25f * S(100 * Math.Max(.4, 1 - t * .3), t) * Mathf.Clamp01(1f - (float)t / 1.6f);   // hum sagging out
        }
        return Normalize(o, .8f);
    }

    /// <summary>A boot on steel deck plate: a heel thump, the plate ringing briefly, grit, a lighter toe.</summary>
    static float[] Step(int v)
    {
        var rng = new System.Random(70 + v);
        int n = (int)(.35f * Rate);
        var o = new float[n];
        void Hit(int at, float level)
        {
            float f0 = 75f + v * 4f;
            for (int i = 0; at + i < n && i < Rate / 8; i++)
            {
                double t = (double)i / Rate;
                o[at + i] += level * (float)(Math.Sin(2 * Math.PI * f0 * (1 - .3 * Math.Min(1, t / .04)) * t) * Math.Exp(-t / .05));
            }
            var imp = new float[n];
            for (int i = 0; i < 4; i++) imp[at + i] = (float)(rng.NextDouble() * 2 - 1);
            foreach (float f in new[] { 380f + v * 70f, 610f + v * 55f, 1040f - v * 40f })
            {
                var m = Mode(imp, f, .1f);
                for (int i = 0; i < n; i++) o[i] += m[i] * level * .25f;
            }
            for (int i = 0; i < Rate / 120; i++) o[at + i] += level * .15f * (float)(rng.NextDouble() * 2 - 1);
        }
        Hit(0, 1f);
        Hit((int)((.07 + v * .008) * Rate), .45f);
        return Normalize(o, .55f);
    }

    // ================= guns =================

    /// <summary>The recorded shot, pitched up a touch with its long black-powder tail cut short (the room's
    /// reverb adds the space back), a slide clack and a little punch underneath.</summary>
    static float[] PistolShot(float[] src, int v)
    {
        var x = Resample(src, 1.12 + v * .02);
        int n = Mathf.Min(x.Length, (int)(.5f * Rate));
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            o[i] = x[i] * (t < .16f ? 1f : Mathf.Clamp01(1f - (t - .16f) / .26f));
        }
        var rng = new System.Random(80 + v);
        var imp = Impulse(n, (int)(.02f * Rate), 3, rng);
        foreach (float f in new[] { 2400f, 3600f })
        {
            var m = Mode(imp, f, .03f);
            for (int i = 0; i < n; i++) o[i] += m[i] * .2f;
        }
        for (int i = 0; i < (int)(.05f * Rate); i++) o[i] += .35f * S(90, (double)i / Rate) * (float)Math.Exp(-i / (.02 * Rate));
        return Normalize(o, .75f);
    }

    /// <summary>The recorded shot pitched way down into a boom, a sub thump and a burst of blast under it,
    /// then the pump racked.</summary>
    static float[] ShotgunShot(float[] src, float[] pump, int v)
    {
        var x = Resample(src, .72 - v * .03);
        var rng = new System.Random(90 + v);
        var blast = Lowpass(White((int)(.09f * Rate), rng), 1800f);
        int pumpAt = (int)(.48f * Rate);
        int n = Mathf.Max((int)(.8f * Rate), pumpAt + pump.Length);
        var o = new float[n];
        for (int i = 0; i < Mathf.Min(n, x.Length); i++)
        {
            float t = (float)i / Rate;
            o[i] = x[i] * (t < .25f ? 1f : Mathf.Clamp01(1f - (t - .25f) / .4f));
        }
        for (int i = 0; i < blast.Length; i++) o[i] += blast[i] * (1f - (float)i / blast.Length) * .8f;
        for (int i = 0; i < (int)(.15f * Rate); i++)
        {
            double t = (double)i / Rate;
            o[i] += .9f * (float)(Math.Sin(2 * Math.PI * (60 - 130 * t) * t) * Math.Exp(-t / .06));
        }
        for (int i = 0; i < n; i++) o[i] = (float)Math.Tanh(o[i] * 1.6f);
        float peak = Peak(o, 0, pumpAt);
        for (int i = 0; i < pump.Length; i++) o[pumpAt + i] += pump[i] * peak * .55f;
        return Normalize(o, .8f);
    }

    /// <summary>The pump racked back and slammed home: two scrapes, two clacks.</summary>
    static float[] Pump()
    {
        var rng = new System.Random(100);
        int n = (int)(.42f * Rate);
        var o = new float[n];
        void Stroke(int at, float[] modes, float level, float body)
        {
            var scrape = Bandpass(White((int)(.06f * Rate), rng), 3200f, 2f);
            for (int i = 0; i < scrape.Length; i++) o[at + i] += scrape[i] * ((float)i / scrape.Length) * level * .6f;
            var imp = Impulse(n, at + scrape.Length, 5, rng);
            foreach (float f in modes)
            {
                var m = Mode(imp, f, .07f);
                for (int i = 0; i < n; i++) o[i] += m[i] * level * .5f;
            }
            if (body > 0f)
                for (int i = 0; i < (int)(.06f * Rate) && at + scrape.Length + i < n; i++)
                    o[at + scrape.Length + i] += body * S(140, (double)i / Rate) * (float)Math.Exp(-i / (.02 * Rate));
        }
        Stroke(0, new[] { 950f, 2200f, 3100f }, .8f, 0f);
        Stroke((int)(.2f * Rate), new[] { 820f, 1900f, 2900f }, 1f, .5f);
        return Normalize(o, .8f);
    }

    /// <summary>Under the energy guns' zap (GunFX): a short thump, a snap and a crackle, no black powder.</summary>
    static float[] EnergyShot(int v)
    {
        var rng = new System.Random(110 + v);
        int n = (int)(.35f * Rate);
        var o = new float[n];
        for (int i = 0; i < (int)(.12f * Rate); i++)
        {
            double t = (double)i / Rate;
            o[i] += (float)(Math.Sin(2 * Math.PI * (85 - 200 * t) * t) * Math.Exp(-t / .035));
        }
        var snap = Bandpass(White((int)(.02f * Rate), rng), 2800f + v * 300f, .9f);
        for (int i = 0; i < snap.Length; i++) o[i] += snap[i] * (1f - (float)i / snap.Length) * 1.2f;
        for (float t = .008f; t < .05f; t += (float)(.002 + rng.NextDouble() * .006))
            o[(int)(t * Rate)] += (float)(rng.NextDouble() * 2 - 1) * .8f;
        for (int i = 0; i < (int)(.12f * Rate); i++) o[i] += .12f * S(520 + v * 40, (double)i / Rate) * (float)Math.Exp(-i / (.04 * Rate));
        return Normalize(o, .7f);
    }

    static float[] MagOut()
    {
        var rng = new System.Random(120);
        int n = (int)(.3f * Rate);
        var o = new float[n];
        var imp = Impulse(n, 0, 3, rng);
        foreach (float f in new[] { 2600f, 4100f })
        {
            var m = Mode(imp, f, .025f);
            for (int i = 0; i < n; i++) o[i] += m[i] * .5f;
        }
        var slide = Bandpass(White((int)(.12f * Rate), rng), 2500f, 1.5f);
        int at = (int)(.03f * Rate);
        for (int i = 0; i < slide.Length; i++) o[at + i] += slide[i] * (1f - (float)i / slide.Length) * .4f;
        for (int i = 0; i < (int)(.05f * Rate); i++) o[(int)(.2f * Rate) + i] += .3f * S(160, (double)i / Rate) * (float)Math.Exp(-i / (.015 * Rate));
        return Normalize(o, .7f);
    }

    static float[] MagIn(float weight)
    {
        var rng = new System.Random(130);
        int n = (int)(.25f * Rate);
        var o = new float[n];
        var scrape = Bandpass(White((int)(.05f * Rate), rng), 2800f, 1.5f);
        for (int i = 0; i < scrape.Length; i++) o[i] += scrape[i] * ((float)i / scrape.Length) * .35f;
        var imp = Impulse(n, scrape.Length, 4, rng);
        foreach (float f in new[] { 1100f, 2500f })
        {
            var m = Mode(imp, f, .06f);
            for (int i = 0; i < n; i++) o[i] += m[i] * .6f * weight;
        }
        for (int i = 0; i < (int)(.05f * Rate); i++) o[scrape.Length + i] += .45f * weight * S(120, (double)i / Rate) * (float)Math.Exp(-i / (.02 * Rate));
        return Normalize(o, .75f);
    }

    static float[] SlideRelease()
    {
        var rng = new System.Random(140);
        int n = (int)(.3f * Rate);
        var o = new float[n];
        var imp = Impulse(n, 0, 4, rng);
        foreach (var (f, d) in new[] { (1600f, .08f), (3300f, .05f), (5000f, .03f) })
        {
            var m = Mode(imp, f, d);
            for (int i = 0; i < n; i++) o[i] += m[i] * .5f;
        }
        var spring = Bandpass(White((int)(.04f * Rate), rng), 6000f, 2f);
        for (int i = 0; i < spring.Length; i++) o[(int)(.004f * Rate) + i] += spring[i] * .25f;
        for (int i = 0; i < (int)(.06f * Rate); i++) o[i] += .35f * S(180, (double)i / Rate) * (float)Math.Exp(-i / (.02 * Rate));
        return Normalize(o, .8f);
    }

    /// <summary>An energy cell seated, then a short charge whine rising to a ready ping.</summary>
    static float[] CellIn()
    {
        var click = MagIn(.8f);
        int n = (int)(.65f * Rate);
        var o = new float[n];
        for (int i = 0; i < click.Length; i++) o[i] += click[i];
        int at = (int)(.08f * Rate), whine = (int)(.42f * Rate);
        double phase = 0;
        for (int i = 0; i < whine; i++)
        {
            float t = (float)i / whine;
            double f = 280 + 1120 * t * t;
            phase += 2 * Math.PI * f / Rate;
            o[at + i] += (float)(Math.Sin(phase) + .3 * Math.Sin(phase * 2) + .15 * Math.Sin(phase * 3)) * .22f * t;
        }
        for (int i = 0; i < (int)(.1f * Rate) && at + whine + i < n; i++)
            o[at + whine + i] += .3f * S(2000, (double)i / Rate) * (float)Math.Exp(-i / (.03 * Rate));
        return Normalize(o, .7f);
    }

    // ================= DSP =================

    static float S(double f, double t, double phase = 0) => (float)Math.Sin(2 * Math.PI * f * t + phase);

    static float[] White(int n, System.Random rng)
    {
        var o = new float[n];
        for (int i = 0; i < n; i++) o[i] = (float)(rng.NextDouble() * 2 - 1);
        return o;
    }

    static float[] Brown(int n, System.Random rng)
    {
        var o = new float[n];
        float y = 0f;
        for (int i = 0; i < n; i++) { y = y * .998f + (float)(rng.NextDouble() * 2 - 1) * .05f; o[i] = y; }
        return o;
    }

    static float[] Impulse(int n, int at, int width, System.Random rng)
    {
        var o = new float[n];
        for (int i = 0; i < width && at + i < n; i++) o[at + i] = (float)(rng.NextDouble() * 2 - 1);
        return o;
    }

    /// <summary>RBJ biquad, run over a whole buffer.</summary>
    static float[] Biquad(float[] x, double b0, double b1, double b2, double a0, double a1, double a2)
    {
        var y = new float[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double v = (b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1; x1 = x[i]; y2 = y1; y1 = v;
            y[i] = (float)v;
        }
        return y;
    }

    static float[] Lowpass(float[] x, double f, double q = .707)
    {
        double w = 2 * Math.PI * f / Rate, c = Math.Cos(w), a = Math.Sin(w) / (2 * q);
        return Biquad(x, (1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
    }

    static float[] Highpass(float[] x, double f, double q = .707)
    {
        double w = 2 * Math.PI * f / Rate, c = Math.Cos(w), a = Math.Sin(w) / (2 * q);
        return Biquad(x, (1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
    }

    static float[] Bandpass(float[] x, double f, double q)
    {
        double w = 2 * Math.PI * f / Rate, c = Math.Cos(w), a = Math.Sin(w) / (2 * q);
        return Biquad(x, a, 0, -a, 1 + a, -2 * c, 1 - a);
    }

    /// <summary>A ringing mode: a band-pass narrow enough to ring for about <paramref name="decay"/> seconds.</summary>
    static float[] Mode(float[] x, double f, double decay)
    {
        double q = Math.Max(.5, Math.PI * f * decay);
        var y = Bandpass(x, f, q);
        double g = Math.Sqrt(q);                                   // keep narrow modes from vanishing
        for (int i = 0; i < y.Length; i++) y[i] = (float)(y[i] * g);
        return y;
    }

    /// <summary>A ringing mode whose pitch follows glide(t): coefficients updated every 32 samples.</summary>
    static float[] GlidingResonator(float[] x, double f, double decay, Func<float, float> glide)
    {
        var y = new float[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0, b0 = 0, b2 = 0, a0 = 1, a1 = 0, a2 = 0;
        double q = Math.Max(.5, Math.PI * f * decay), g = Math.Sqrt(q);
        for (int i = 0; i < x.Length; i++)
        {
            if (i % 32 == 0)
            {
                double fi = f * glide((float)i / Rate), w = 2 * Math.PI * fi / Rate, c = Math.Cos(w), a = Math.Sin(w) / (2 * q);
                b0 = a; b2 = -a; a0 = 1 + a; a1 = -2 * c; a2 = 1 - a;
            }
            double v = (b0 * x[i] + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1; x1 = x[i]; y2 = y1; y1 = v;
            y[i] = (float)(v * g);
        }
        return y;
    }

    /// <summary>A small ceiling speaker: nothing under 300 Hz or over 3.5 kHz.</summary>
    static float[] Speaker(float[] x) => Lowpass(Highpass(x, 300f), 3500f);

    static float[] Resample(float[] x, double ratio)
    {
        int n = (int)(x.Length / ratio);
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            double p = i * ratio;
            int k = (int)p;
            float f = (float)(p - k);
            o[i] = k + 1 < x.Length ? x[k] * (1 - f) + x[k + 1] * f : x[Math.Min(k, x.Length - 1)];
        }
        return o;
    }

    static float Peak(float[] x, int from = 0, int to = -1)
    {
        if (to < 0) to = x.Length;
        float p = 0f;
        for (int i = from; i < to; i++) p = Math.Max(p, Math.Abs(x[i]));
        return p;
    }

    static float[] Normalize(float[] x, float peak)
    {
        float p = Peak(x);
        if (p < 1e-6f) return x;
        for (int i = 0; i < x.Length; i++) x[i] *= peak / p;
        return x;
    }

    /// <summary>Crossfades the end into the start so the clip loops without a click.</summary>
    static float[] Loop(float[] x, float fadeSeconds)
    {
        int f = (int)(fadeSeconds * Rate), n = x.Length - f;
        var o = new float[n];
        Array.Copy(x, o, n);
        for (int i = 0; i < f; i++)
        {
            float t = (float)i / f;
            o[i] = x[i] * Mathf.Sqrt(t) + x[n + i] * Mathf.Sqrt(1f - t);
        }
        return o;
    }

    // ================= files =================

    static string Save(string path, float[] samples, bool loop)
    {
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int bytes = samples.Length * 2;
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + bytes);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(bytes);
            foreach (float s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
        }
        AssetDatabase.ImportAsset(path);
        var imp = (AudioImporter)AssetImporter.GetAtPath(path);
        imp.forceToMono = true;
        var s2 = imp.defaultSampleSettings;
        s2.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
        s2.compressionFormat = loop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
        s2.quality = .7f;
        imp.defaultSampleSettings = s2;
        imp.SaveAndReimport();
        float rms = 0f;
        foreach (float s in samples) rms += s * s;
        rms = Mathf.Sqrt(rms / Mathf.Max(1, samples.Length));
        return $"{Path.GetFileName(path)}: {samples.Length / (float)Rate:F2} s, peak {Peak(samples):F2}, rms {rms:F3}\n";
    }

    /// <summary>A WAV file as mono floats at 44.1 kHz (PCM 16/24-bit or 32-bit float, any channel count).</summary>
    static float[] ReadWav(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int channels = 1, rate = Rate, bits = 16, format = 1, dataAt = -1, dataLen = 0;
        int p = 12;
        while (p + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
            int len = BitConverter.ToInt32(b, p + 4);
            if (id == "fmt ")
            {
                format = BitConverter.ToInt16(b, p + 8); channels = BitConverter.ToInt16(b, p + 10);
                rate = BitConverter.ToInt32(b, p + 12); bits = BitConverter.ToInt16(b, p + 22);
            }
            else if (id == "data") { dataAt = p + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
            p += 8 + len + (len & 1);
        }
        if (dataAt < 0) throw new Exception("no data chunk in " + path);
        int bytesPer = bits / 8, frames = dataLen / (bytesPer * channels);
        var o = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                int at = dataAt + (i * channels + c) * bytesPer;
                float v = format == 3 ? BitConverter.ToSingle(b, at)
                        : bits == 16 ? BitConverter.ToInt16(b, at) / 32768f
                        : bits == 24 ? ((b[at] | (b[at + 1] << 8) | ((sbyte)b[at + 2] << 16)) / 8388608f)
                        : BitConverter.ToInt32(b, at) / 2147483648f;
                sum += v;
            }
            o[i] = sum / channels;
        }
        return rate == Rate ? o : Resample(o, (double)rate / Rate);
    }
}
