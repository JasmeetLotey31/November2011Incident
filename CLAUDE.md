# November 2011 Incident

Unity 6 game (editor **6000.0.46f1**) built by two people, each with their own Claude Code.
Git + GitHub is the source of truth: https://github.com/JasmeetLotey31/November2011Incident (branch `main`).

- The Unity project is the `November 2011/` subfolder, not the repo root. Pass
  `--project-path "November 2011"` (or the absolute path) to `unity` commands.
- Unity tooling: Unity CLI (`unity`) + the `unity@unity-agent-plugin` Claude plugin. Drive the open
  Editor with `unity command ...` (via the `com.unity.pipeline` package) instead of hand-editing
  `.unity` / `.prefab` YAML.

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
