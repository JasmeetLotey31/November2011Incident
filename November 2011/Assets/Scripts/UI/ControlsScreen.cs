using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// "Click to play" title card: the controls and how to survive the keys. The game is frozen
/// behind it until you click. Press Esc in game to pause and bring it back.
/// Shown once per play session; a restart after death drops you straight back in.
/// Built at runtime (no prefab). Put it on any object in the level.
/// </summary>
[DisallowMultipleComponent]
public sealed class ControlsScreen : MonoBehaviour
{
    [SerializeField] string title = "NOVEMBER 2011 INCIDENT";
    [SerializeField] string subtitle = "BLACK BOX RECORDING  //  JUPITER STATION ARMORY  //  13 NOV 2011";

    static bool introShown;

    GameObject root;
    Text headline, prompt;
    FirstPersonController motor;
    PlayerVitals vitals;
    bool showing, paused;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => introShown = false;

    void Awake()
    {
        motor = FindAnyObjectByType<FirstPersonController>();
        vitals = FindAnyObjectByType<PlayerVitals>();
        Build();
        root.SetActive(false);
    }

    void Start()
    {
        if (!introShown) Show(false);
    }

    void OnDestroy()
    {
        if (showing) { Time.timeScale = 1f; AudioListener.pause = false; }
        if (root != null) Destroy(root);
    }

    void Show(bool asPause)
    {
        showing = true;
        paused = asPause;
        headline.text = asPause ? "PAUSED" : title;
        prompt.text = asPause ? "CLICK TO RESUME" : "CLICK TO PLAY";
        root.SetActive(true);
        Time.timeScale = 0f;
        AudioListener.pause = true;
        // The look controller recaptures the mouse on any click; keep it off until we're done.
        if (motor != null) motor.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Hide()
    {
        showing = false;
        introShown = true;
        root.SetActive(false);
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (motor != null) motor.enabled = true; // re-locks the cursor
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        Gamepad pad = Gamepad.current;
        if (showing)
        {
            // On release, so the click that starts the game doesn't also fire the gun.
            bool go = (mouse != null && mouse.leftButton.wasReleasedThisFrame)
                      || (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
                      || (pad != null && pad.startButton.wasPressedThisFrame)
                      || (paused && keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
            if (go) Hide();
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                prompt.color = Color.Lerp(HudKit.Ivory, HudKit.Danger, Mathf.PingPong(Time.unscaledTime * 1.6f, 1f));
            }
            return;
        }
        bool dead = vitals != null && vitals.IsDead;
        if (!dead && ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame)))
            Show(true);
    }

    void Build()
    {
        root = HudKit.Canvas("Controls Screen", 1000);
        Transform t = root.transform;
        Image dim = HudKit.Img("Dim", t, new Color(.02f, 0f, 0f, .93f));
        HudKit.Stretch(dim.rectTransform);
        var vignette = HudKit.Rect("Vignette", t).gameObject.AddComponent<RawImage>();
        vignette.raycastTarget = false;
        vignette.texture = HudKit.Vignette();
        vignette.color = new Color(0f, 0f, 0f, 1f);
        HudKit.Stretch(vignette.rectTransform);

        headline = HudKit.Txt("Title", t, 84, TextAnchor.MiddleCenter);
        headline.color = HudKit.Danger;
        HudKit.Place(headline.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -78f), new Vector2(1600f, 110f));
        Text sub = HudKit.Txt("Subtitle", t, 22, TextAnchor.MiddleCenter, FontStyle.Normal);
        sub.text = subtitle;
        HudKit.Place(sub.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -192f), new Vector2(1600f, 30f));

        Column(t, "CONTROLS", new Vector2(-470f, 250f), new[]
        {
            Key("W A S D", "move"),
            Key("SHIFT", "sprint"),
            Key("SPACE", "jump"),
            Key("MOUSE", "look"),
            Key("LEFT CLICK", "shoot"),
            Key("RIGHT CLICK", "aim (laser sight)"),
            Key("R", "reload"),
            Key("1 / 2  or  WHEEL", "switch guns (you carry two)"),
            Key("E", "grab ammo  /  HOLD at a locker to buy"),
            Key("LEFT ALT", "dash (you can't be hit mid-dash)"),
            Key("V", "kick / pry a mouth open"),
            Key("F", "flashlight"),
            Key("ESC", "pause"),
        });

        Column(t, "HOW TO SURVIVE THE KEYS", new Vector2(430f, 250f), new[]
        {
            Tip("Every hit and kill earns POINTS. Spend them at the armory's weapon lockers: hold " + K("E") + "."),
            Tip("Shoot INTO the open mouth. The throat is the weak spot; the blade is armoured."),
            Tip("Staggered, or stuck in the floor after a dive?  " + K("V") + " KICK it."),
            Tip("Red glow on the floor under you = one is diving on you.  MOVE."),
            Tip("Tongue swinging at you?  " + K("SPACE") + " jump it, or " + K("ALT") + " dash."),
            Tip("Tongue speared you?  Shoot the tongue, or " + K("ALT") + " dash to rip free."),
            Tip("Knocked down?  " + K("ALT") + " to roll up.  Down or nearly dead, they swallow you."),
            Tip("Pinned?  Mash " + K("A") + " " + K("D") + " to shove it back, then " + K("V") + " STAB it."),
            Tip("Swallowed?  Shoot the throat or mash " + K("V") + ".  Too slow and it bites a chunk off."),
            Tip("Gore spit blinds you and the puddles burn.  Keep moving."),
            Tip("When one screams the lights die and they ALL come.  Flashlight " + K("F") + "."),
            Tip("They eat the dead and grow.  Don't let them feed."),
            Tip("Dead keys drop scrap: ammo and health."),
        });

        prompt = HudKit.Txt("Prompt", t, 52, TextAnchor.MiddleCenter);
        HudKit.Place(prompt.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 90f), new Vector2(1200f, 70f));
    }

    static void Column(Transform parent, string heading, Vector2 centre, string[] lines)
    {
        Text h = HudKit.Txt(heading, parent, 30, TextAnchor.UpperLeft);
        h.color = HudKit.Danger;
        h.text = heading;
        HudKit.Place(h.rectTransform, new Vector2(.5f, .5f), centre, new Vector2(800f, 40f));
        Text body = HudKit.Txt(heading + " Body", parent, 24, TextAnchor.UpperLeft, FontStyle.Normal);
        body.supportRichText = true;
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.lineSpacing = 1.1f;
        body.text = string.Join("\n", lines);
        HudKit.Place(body.rectTransform, new Vector2(.5f, .5f), centre + new Vector2(0f, -310f), new Vector2(800f, 560f));
    }

    static string K(string key) => $"<color=#FF5A45><b>[{key}]</b></color>";
    static string Key(string key, string what) => $"<color=#FF5A45><b>{key}</b></color>   {what}";
    static string Tip(string text) => "•  " + text;
}
