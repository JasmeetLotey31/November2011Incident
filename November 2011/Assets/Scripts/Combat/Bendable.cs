using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The bend meter from the combat loop. Hits fill the meter; when it is full the target jams
/// (falls silent and stops) for a few seconds, longer if it was pinned to a wall.
/// Keys keep this on their root and react to <see cref="Jammed"/> / <see cref="Recovered"/>.
/// Keys can't be killed: once the jam wears off, the meter resets and it gets back up.
/// </summary>
public class Bendable : MonoBehaviour, IBendable
{
    [Tooltip("Meter fill needed to jam. A nail fills 0.5 by default, so 2 nails jam.")]
    [SerializeField] float maxBend = 1f;
    [Tooltip("Seconds a normal jam lasts.")]
    [SerializeField] float jamDuration = 5f;
    [Tooltip("Seconds a jam lasts when the hit pinned the target to a wall.")]
    [SerializeField] float pinnedJamDuration = 10f;
    [Tooltip("Meter drained per second once the target hasn't been hit for a while (0 = never).")]
    [SerializeField] float bendDecayPerSecond = 0.15f;
    [SerializeField] float decayDelay = 3f;

    [Header("Events")]
    public UnityEvent onJammed;
    public UnityEvent onRecovered;

    /// <summary>Raised with the hit that caused the jam (check <c>hit.pinned</c>).</summary>
    public event Action<BendHit> Jammed;
    public event Action Recovered;

    public float Bend { get; private set; }
    public float Bend01 => maxBend > 0f ? Bend / maxBend : 0f;
    public bool IsJammed { get; private set; }
    public bool IsPinned { get; private set; }
    public Vector3 PinPoint { get; private set; }
    public float JamTimeRemaining { get; private set; }

    float lastHitTime = float.NegativeInfinity;

    public void ApplyBend(BendHit hit)
    {
        lastHitTime = Time.time;

        if (IsJammed)
        {
            // Pinning an already-jammed target nails it down for the longer duration.
            if (hit.pinned && !IsPinned)
            {
                IsPinned = true;
                PinPoint = hit.pinPoint;
                JamTimeRemaining = Mathf.Max(JamTimeRemaining, pinnedJamDuration);
            }
            return;
        }

        Bend = Mathf.Min(maxBend, Bend + hit.amount);
        if (Bend >= maxBend)
            Jam(hit);
    }

    void Jam(BendHit hit)
    {
        IsJammed = true;
        IsPinned = hit.pinned;
        PinPoint = hit.pinPoint;
        JamTimeRemaining = hit.pinned ? pinnedJamDuration : jamDuration;
        Jammed?.Invoke(hit);
        onJammed.Invoke();
    }

    /// <summary>Ends the jam now: the target wakes up with an empty meter.</summary>
    public void Recover()
    {
        if (!IsJammed) return;
        IsJammed = false;
        IsPinned = false;
        JamTimeRemaining = 0f;
        Bend = 0f;
        Recovered?.Invoke();
        onRecovered.Invoke();
    }

    void Update()
    {
        if (IsJammed)
        {
            JamTimeRemaining -= Time.deltaTime;
            if (JamTimeRemaining <= 0f)
                Recover();
        }
        else if (Bend > 0f && bendDecayPerSecond > 0f && Time.time - lastHitTime > decayDelay)
        {
            Bend = Mathf.Max(0f, Bend - bendDecayPerSecond * Time.deltaTime);
        }
    }
}
