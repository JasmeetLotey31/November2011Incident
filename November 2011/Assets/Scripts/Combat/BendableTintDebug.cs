using UnityEngine;

/// <summary>
/// Test-dummy feedback until the keys have real art: tints a renderer by the bend meter
/// (white → orange), blue while jammed, cyan while pinned. Not for final enemies.
/// </summary>
[RequireComponent(typeof(Bendable))]
public class BendableTintDebug : MonoBehaviour
{
    [SerializeField] Renderer target;
    [SerializeField] Color normalColor = Color.white;
    [SerializeField] Color bentColor = new Color(1f, 0.45f, 0.1f);
    [SerializeField] Color jammedColor = new Color(0.2f, 0.35f, 1f);
    [SerializeField] Color pinnedColor = new Color(0.1f, 0.95f, 1f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    Bendable bendable;
    MaterialPropertyBlock block;

    void Awake()
    {
        bendable = GetComponent<Bendable>();
        if (target == null) target = GetComponentInChildren<Renderer>();
        block = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        if (target == null) return;
        Color c = bendable.IsPinned ? pinnedColor
                : bendable.IsJammed ? jammedColor
                : Color.Lerp(normalColor, bentColor, bendable.Bend01);

        target.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c); // URP Lit
        block.SetColor(ColorId, c);     // Built-in fallback
        target.SetPropertyBlock(block);
    }
}
