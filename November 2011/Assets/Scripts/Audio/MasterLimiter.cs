using UnityEngine;

/// <summary>
/// A peak limiter on the final mix (put it next to the AudioListener). Several keys screaming on top
/// of a shotgun blast add up past full scale and crackle; this holds the sum under the ceiling,
/// clamping instantly and letting go smoothly, so loud moments stay loud without distorting.
/// </summary>
public sealed class MasterLimiter : MonoBehaviour
{
    [Tooltip("Highest level the mix may reach (linear, 1 = full scale).")]
    [Range(.5f, 1f)] [SerializeField] float ceiling = .85f;
    [Tooltip("Seconds to let the gain back up after a peak.")]
    [SerializeField] float release = .25f;

    /// <summary>Loudest sample seen going in and coming out since the last reset (for tuning the mix).</summary>
    public static float PeakIn, PeakOut;

    float envelope, releaseCoef;

    void Awake() => releaseCoef = Mathf.Exp(-1f / (Mathf.Max(.01f, release) * AudioSettings.outputSampleRate));

    void OnAudioFilterRead(float[] data, int channels)
    {
        float c = ceiling, rc = releaseCoef, env = envelope, maxIn = PeakIn, maxOut = PeakOut;
        for (int i = 0; i < data.Length; i += channels)
        {
            float peak = 0f;
            for (int k = 0; k < channels; k++) { float a = data[i + k] < 0f ? -data[i + k] : data[i + k]; if (a > peak) peak = a; }
            env = peak > env ? peak : env * rc + peak * (1f - rc);
            float gain = env > c ? c / env : 1f;
            for (int k = 0; k < channels; k++) data[i + k] *= gain;
            if (peak > maxIn) maxIn = peak;
            if (peak * gain > maxOut) maxOut = peak * gain;
        }
        envelope = env; PeakIn = maxIn; PeakOut = maxOut;
    }
}
