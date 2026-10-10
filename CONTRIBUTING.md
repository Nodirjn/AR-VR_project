# Contributing

Thank you for considering a contribution. Keep changes focused and preserve Unity's asset references and project configuration.

## Before changing the project

1. Use the Unity Editor version recorded in `ProjectSettings/ProjectVersion.txt` (currently 6000.3.12f1).
2. Read the relevant scene, script, and package settings before editing. The enabled build scene is `Assets/Scenes/SampleScene.unity`.
3. Avoid committing generated folders such as `Library/`, `Temp/`, `Obj/`, `Logs/`, and `Build/`; these are excluded by `.gitignore`.

## Git workflow

1. Create a focused branch from the current default branch, for example `docs/update-setup` or `fix/car-explorer-selection`.
2. Make a small, coherent change. For Unity assets, include their `.meta` files and preserve existing GUIDs; do not regenerate or delete metadata for assets that remain in use.
3. Review `git status` and `git diff` before committing. Confirm the change contains no generated data, credentials, local editor preferences, or unrelated files.
4. Use a concise commit subject in imperative form, such as `Document XR input bindings`.
5. Open a pull request with the purpose, affected scenes/scripts/settings, verification performed, and any platform or device limitations.

## Unity-specific guidance

- Do not edit package versions or project settings as incidental cleanup; describe the reason and impact when such a change is needed.
- Keep the named `CarConcept` root and the model node names expected by `CarExplorerController` aligned when changing the car hierarchy.
- When changing interaction behavior, state which desktop and XR inputs were checked. Do not claim Editor, build, or device verification unless it was actually performed.
- Keep third-party package samples and imported assets intact unless the change requires them. Check their original license terms before redistributing modified or copied material.

## Reporting a change

Include reproduction steps for behavior changes, list the Unity version and target platform used for verification, and attach screenshots or logs when they help reviewers understand the result. For documentation-only changes, identify the files reviewed and the statements that were confirmed from repository contents.
