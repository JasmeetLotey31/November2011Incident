using System;
using UnityEngine;

/// <summary>A sound the keys can hear: where it happened and how far it carries.</summary>
public struct NoiseEvent
{
    public Vector3 position;
    /// <summary>Distance in metres at which the noise can still be heard.</summary>
    public float radius;
    public GameObject source;
}

/// <summary>
/// The noise system from the combat loop. Anything loud (gunfire, doors, sprinting) calls
/// <see cref="Emit"/>; the keys' AI subscribes to <see cref="Emitted"/> to hunt by sound.
/// </summary>
public static class Noise
{
    public static event Action<NoiseEvent> Emitted;

    public static void Emit(Vector3 position, float radius, GameObject source)
    {
        if (radius <= 0f) return;
        Emitted?.Invoke(new NoiseEvent { position = position, radius = radius, source = source });
    }

    // Static events survive Play mode restarts when domain reload is off; clear them on start.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Emitted = null;
}
