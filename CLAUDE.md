# November 2011 Incident

Unity 6 game (editor **6000.6.11f1**, URP) built by two people, each with their own Claude Code.
Both machines must use that exact editor version (Unity Hub → Installs).
Git + GitHub is the source of truth: https://github.com/JasmeetLotey31/November2011Incident (branch `main`).

- The Unity project is the `November 2011/` subfolder, not the repo root. Pass
  `--project-path "November 2011"` (or the absolute path) to `unity` commands.
- Unity tooling: Unity CLI (`unity`) + the `unity@unity-agent-plugin` Claude plugin. Drive the open
  Editor with `unity command ...` (via the `com.unity.pipeline` package) instead of hand-editing
  `.unity` / `.prefab` YAML.

## Art style: PS1 retro, low poly

Everything in the game should look like a PlayStation 1 game. Reference asset: the hand-made key
(`ArtSource/FaceKey/FaceByMyself.blend`, textured version `FaceByMyself_Textured.blend`,
in Unity `Assets/Models/FaceKeyPS1/`).

- **Models:** a few hundred triangles per prop, chunky faceted shapes, flat shading.
  Many copies of props will be on screen; keep counts low.
- **Textures: hand-painted-looking pixel art.** One small atlas per model (256x256 for a prop,
  512x512 for a character), point-filtered. Painted from the geometry
  (`ArtSource/FaceKey/build_pixel_key.py`, `ArtSource/Astronaut/build_pixel_astro.py`):
  warm limited colour ramps, light from the top-left (back faces lit like the front), **smooth
  shading in a few soft steps - no dither/halftone patterns**, soft irregular light/dark blotches
  like brushwork, and only a soft 1-px darker line on really sharp edges (no black outlines, no
  glints). Detail (control panels, hoses, patches, seams) is painted in by 3D position. Picture
  details (faces) are pixelated images (~128 px, 64 colours, no dithering).
  No normal maps, no PBR metal/roughness maps. It should look hand-made, not generated.
- **Unity import:** Filter Mode **Point**, mipmaps **off**, compression **none**, max size = actual size.
- **Materials:** use the shader **`Retro/PS1 Lit`** (`Assets/Shaders/Retro/PS1Lit.shader`) for
  everything: vertex snapping (wobble), affine texture warp, per-vertex lighting, fog, instancing.
  Tweak per material: Vertex Snap Grid, Texture Warp (lower it on big flat polygons), Lighting,
  and turn on **Use mesh colours** for per-triangle-colour models.
  GPU instancing on. Don't use Built-in-pipeline shader packs: this project is URP and they render
  nothing (the "Retro 3D Shader Pack" was removed for that reason; a copy is in G:\Git\RemovedAssets).
- **Screen look** (already set up): `PC_RPAsset`/`Mobile_RPAsset` render scale 0.25 with Point
  upscaling, no MSAA, SSAO off, and a `PS1 Screen` Full Screen Pass on both renderers
  (`Assets/Materials/Retro/PS1_Screen.mat`: colour levels 32 + PS1 4x4 dither). Scene fog: linear.
- **Before exporting from Blender:** apply scale (Ctrl+A, never leave a negative scale), make
  normals point outward (Edit Mode, select all, Shift+N), and save in Object Mode.
- Blender work: hand-made source `.blend` files stay untouched. Scripts write `*_Textured.blend`
  and export FBX into `Assets/Models/<Name>/` (see `ArtSource/FaceKey/*.py` for the pattern).

## One-time setup on each machine (after cloning)

```powershell
irm https://unity.com/install.ps1 | iex           # Unity CLI, if `unity` is missing
claude plugin marketplace add Unity-Technologies/unity-agent-plugin
claude plugin install unity@unity-agent-plugin
git lfs install
unity vcs merge-setup --project-path "November 2011"   # smart-merge scenes/prefabs (local git config)
unity vcs hooks install --project-path "November 2011" # Unity integrity checks on commit/pull
unity status --project-path "November 2011"            # with the Editor open; expect state "ready"
```

The merge driver and hooks live in `.git/`, which is not shared, so each person runs these once.

## Working together: the routine

1. **Pull before starting work** and before committing:
   `unity vcs sync --project-path "November 2011"` (pulls + LFS, and reports the reimport).
   It refuses while the Editor has the project open. Then ask the user to **save and close the
   Editor** (or save, and confirm before using `--force`). Plain `git pull --rebase` is the fallback.
2. **Commit small and often**, with a message saying what changed. Check `unity vcs status` first:
   every new asset must be committed together with its `.meta` file.
3. **Push right after committing** (`git push`), so the other person is never far behind.
   If the push is rejected because the remote moved, pull (step 1) and push again.
4. **Never** force-push `main`, rewrite pushed history, or commit anything under `Library/`,
   `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `.plastic/`, or build output (all ignored).

## Avoiding conflicts

- Scenes and prefabs are the main conflict risk. Tell your teammate before doing heavy edits to a
  shared scene (e.g. `SampleScene.unity`). Better yet, put features in their own prefabs or scenes.
- If a pull conflicts: `unity vcs conflicts` lists them, `unity vcs explain <file>` shows what each
  side changed by object name, and `unity vcs resolve <file>` merges with UnityYAMLMerge
  (`--ours` / `--theirs` to take one side). Ask the user before discarding anyone's work.
- `unity vcs diff <scene>` shows readable scene/prefab changes; use it instead of a raw `git diff`.
- Images (`*.png`) go through Git LFS (see `November 2011/.gitattributes`).
