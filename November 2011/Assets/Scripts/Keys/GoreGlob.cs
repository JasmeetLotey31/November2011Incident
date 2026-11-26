using UnityEngine;

/// <summary>
/// A glob of half-eaten gore a key vomits at you. Arcs through the air. Hits you: damage and gore
/// across your vision. Hits the level: leaves a burning puddle that hurts while you stand in it.
/// Built at runtime by <see cref="Launch"/>; no prefab needed.
/// </summary>
public sealed class GoreGlob : MonoBehaviour
{
    float damage, puddleDps, puddleSeconds;
    Vector3 velocity;
    GameObject source;
    float age;

    public static void Launch(Vector3 from, Vector3 velocity, float damage, float puddleDps, float puddleSeconds, Material material, GameObject source)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Gore Glob";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = from;
        go.transform.localScale = Vector3.one * Random.Range(.22f, .32f);
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
        var glob = go.AddComponent<GoreGlob>();
        glob.velocity = velocity;
        glob.damage = damage;
        glob.puddleDps = puddleDps;
        glob.puddleSeconds = puddleSeconds;
        glob.source = source;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age > 4f) { Destroy(gameObject); return; }
        velocity += Physics.gravity * dt;
        Vector3 step = velocity * dt;
        transform.localScale = Vector3.one * (transform.localScale.x * (1f + Mathf.Sin(age * 30f) * .02f)); // wobbling

        KeyTarget player = KeyTarget.Current;
        if (player != null && !player.IsDead)
        {
            Vector3 chest = player.Position + Vector3.up * 1.2f;
            if (Vector3.Distance(transform.position + step * .5f, chest) < .75f)
            {
                player.Health.TakeDamage(new DamageInfo { amount = damage, point = transform.position, direction = velocity.normalized, source = source });
                var eaten = player.GetComponent<PlayerEaten>();
                if (eaten != null) eaten.Splattered();
                BloodVFX.Spray(transform.position, -velocity, BloodVFX.KeyIchor, 1.5f);
                Destroy(gameObject);
                return;
            }
        }

        if (Physics.Raycast(transform.position, step.normalized, out RaycastHit hit, step.magnitude + .1f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<KeyFlyer>() == null)
        {
            BloodVFX.Spray(hit.point, hit.normal, BloodVFX.KeyIchor, 1.2f);
            if (Vector3.Dot(hit.normal, Vector3.up) > .5f) AcidPuddle.Create(hit.point, puddleDps, puddleSeconds, source);
            Destroy(gameObject);
            return;
        }
        transform.position += step;
    }
}

/// <summary>A burning puddle of gore: hurts while you stand in it, then fades.</summary>
public sealed class AcidPuddle : MonoBehaviour
{
    float dps, until, radius;
    GameObject source;
    Light glow;

    public static void Create(Vector3 at, float dps, float seconds, GameObject source)
    {
        var go = new GameObject("Burning Gore");
        go.transform.position = at;
        var p = go.AddComponent<AcidPuddle>();
        p.dps = dps;
        p.until = Time.time + seconds;
        p.radius = 1.2f;
        p.source = source;
        BloodVFX.Pool(at, new Color(.25f, .2f, .02f, 1f), 2.2f);
        var l = new GameObject("Glow").AddComponent<Light>();
        l.transform.SetParent(go.transform, false);
        l.transform.localPosition = Vector3.up * .3f;
        l.type = LightType.Point; l.color = new Color(.9f, .6f, .1f); l.range = 2.5f; l.intensity = 1.5f; l.shadows = LightShadows.None;
        p.glow = l;
        Destroy(go, seconds + .1f);
    }

    float nextTick;

    void Update()
    {
        if (glow != null) glow.intensity = 1.2f + Mathf.PerlinNoise(Time.time * 6f, 0f) * .8f;
        KeyTarget player = KeyTarget.Current;
        if (player == null || player.IsDead || Time.time > until || Time.time < nextTick) return;
        Vector3 d = player.Position - transform.position; d.y = 0f;
        if (d.magnitude <= radius && Mathf.Abs(player.Position.y - transform.position.y) < .6f)
        {
            // Burns in ticks (not every frame) so the hurt feedback reads clearly.
            nextTick = Time.time + .5f;
            player.Health.TakeDamage(new DamageInfo { amount = dps * .5f, point = player.Position, direction = Vector3.up, source = source });
        }
    }
}
