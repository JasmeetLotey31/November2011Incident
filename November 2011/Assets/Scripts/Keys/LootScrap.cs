using UnityEngine;

/// <summary>
/// A glowing brass scrap dropped by a dead key. Walk near it and it flies to you: rounds for the
/// sidearm and/or a bit of health. No button press: you grab loot mid-fight, which is the point.
/// </summary>
public sealed class LootScrap : MonoBehaviour
{
    [SerializeField] int rounds = 2;
    [SerializeField] float heal = 0f;
    [SerializeField] float magnetRadius = 3.5f;
    [SerializeField] float collectRadius = .7f;
    [SerializeField] float lifetime = 30f;
    [SerializeField] AudioClip pickupClip;

    Vector3 velocity;
    float spawnTime, groundY;
    bool landed;

    public void Setup(int newRounds, float newHeal, Vector3 launch)
    {
        rounds = newRounds;
        heal = newHeal;
        velocity = launch;
    }

    void Start()
    {
        spawnTime = Time.time;
        groundY = Physics.Raycast(transform.position + Vector3.up * .5f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y + .25f
            : transform.position.y;
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        transform.Rotate(0f, 160f * dt, 0f, Space.World);
        KeyTarget player = KeyTarget.Current;

        // A short arc out of the body, then hover-bob on the floor.
        if (!landed)
        {
            velocity += Vector3.down * 12f * dt;
            transform.position += velocity * dt;
            if (transform.position.y <= groundY && velocity.y < 0f)
            {
                landed = true;
                transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
            }
        }
        else
        {
            Vector3 p = transform.position;
            p.y = groundY + Mathf.Sin((Time.time - spawnTime) * 4f) * .05f;
            transform.position = p;
        }

        if (player == null || player.IsDead || Time.time - spawnTime < .35f) return;
        Vector3 chest = player.Position + Vector3.up * 1f;
        float distance = Vector3.Distance(transform.position, chest);
        if (distance <= collectRadius) { Collect(player); return; }
        if (distance <= magnetRadius)
        {
            landed = true; // pulled off the floor
            float speed = Mathf.Lerp(14f, 4f, distance / magnetRadius);
            transform.position = Vector3.MoveTowards(transform.position, chest, speed * dt);
        }
    }

    void Collect(KeyTarget player)
    {
        if (rounds > 0 && player.Gun != null) player.Gun.AddReserve(rounds);
        if (heal > 0f && player.Health != null) player.Health.Heal(heal);
        if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, transform.position, .7f);
        Destroy(gameObject);
    }
}
