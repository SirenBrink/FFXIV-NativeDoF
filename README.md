# Native DoF

Experimental Dalamud plugin that enables FFXIV's native updated depth of field during gameplay.

## Compatibility

Version **0.1.8**, **Dalamud API 15**, Windows x64. Built against Dalamud 15.0.3.9 and .NET 10.

This prototype supports only game build **2026.10.16.0000.0000**, with `ffxiv_dx11.exe` SHA256:

`46A52AB1DBE80B9A8D6C4D4531BF66693904200C5135FACACD053EF28A5DE14A`

Other executables are rejected before native hooks are created. Game updates require fresh analysis and a plugin update. Do not bypass that check.

Version 0.1.8 updates the native scene/render addresses and manager pointer for the October 16 game build and compiles against Dalamud 15.0.3.9 (still API 15). DoF layout and scene guards were checked against the updated executable and its bundled FFXIVClientStructs definitions. The build and 46 regression checks pass, and gameplay behavior on this patch has been confirmed in-game.

Gameplay DoF was visually confirmed in 0.1.2. Version 0.1.6 adds independent combat and out-of-combat profiles, presets, optional saved activation, and additional suspension rules. The background blur and close-up look-at override have been visually confirmed by the author in-game. This remains a prototype. Performance, graphics-mod compatibility, and all transition cases have not been systematically tested.

## Install

1. In FFXIV, open `/xlsettings` → **Experimental**.
2. Under **Custom Plugin Repositories**, paste this URL:

   ```text
   https://raw.githubusercontent.com/SirenBrink/FFXIV-NativeDoF/main/repo.json
   ```

3. Click **+**, ensure the repository is enabled, then **Save**.
4. Open `/xlplugins`, search for **Native DoF Prototype**, and click **Install**.
5. Run `/nativedof` and enable gameplay depth of field.

The effect starts off by default. In 0.1.6, **Remember enabled state between sessions** optionally restores your last on/off choice. This is a third-party repository shown through Dalamud's in-game plugin installer; it is not part of Dalamud's official plugin repository. The compatibility limits above still apply.

**Already using the developer plugin?** Disable that copy and remove its DLL entry from **Dev Plugin Locations** before installing through the custom repository, so two copies cannot load together. Note your settings before switching.

## Controls

Version **0.1.8** includes −/+ buttons beside every slider: steps of 0.1 for distances, aperture and delay, and 0.01 for background strength. Buttons snap to the next step in the chosen direction and respect the setting's limits. Ctrl-click still allows exact entry.

- `/nativedof`: open controls.
- `/nativedof on`: enable the effect.
- `/nativedof off`: disable the effect.
- `/nativedof toggle`: switch the effect on or off without opening the window.
- `/nativedof subtle`, `balanced`, or `strong`: select an out-of-combat preset without changing activation or the combat profile.
- `/nativedof status`: open controls and write diagnostics to the Dalamud log.

Each profile offers **Background blur curve** or physical camera-focus mode. The background curve controls where blur begins, where it reaches full strength, and its strength from 0–1, with foreground blur disabled. Distances are measured from the camera, not the player. Presets select this mode with distances 15–60 and strengths 0.15 (**Subtle**), 0.35 (**Balanced**), or 0.65 (**Strong**). These experimental values need in-game calibration; they are not measured percentages of visible blur. Increase strength or bring the distances closer for a stronger effect.

Uncheck **Background blur curve** for the previous native camera focus or manual focus distance (0.5–500) and aperture (f/1.4–f/32) controls. Physical DoF becomes weak at longer focus distances; the old aperture-only presets proved ineffective for background blur. Ctrl-click a slider to type an exact value. Existing profiles retain their previous mode until you select a new preset or enable the curve.

For background DoF, select **Subtle** in the out-of-combat profile, enable the effect, and optionally enable **Remember enabled state between sessions**. Lower background strength for less blur. Uncheck **Suspend during combat** to reveal independent combat controls. The plugin automatically switches profiles on entering/leaving combat. Existing settings are preserved on upgrade, including the previous combat suspension choice.

DoF always suspends in first-person, cutscenes, GPose, loading and logout. Native cutscene and GPose settings remain under game control. Optional suspension rules cover combat (on by default), duties, and mounting/flying, including riding as a passenger. Duty suspension takes precedence over the combat profile. **Activation delay** (0–30 seconds) waits after enabling or after all suspension rules clear, restarting if another suspension occurs. Suspension and manual off are immediate. The delay is not a fade; switching between two enabled profiles is immediate.

With saved activation enabled, **Turn off now** and `/nativedof off` also save the off state. Temporary suspension does not change your saved choice.

**Focus at look-at point when zoomed in close** optionally overrides either active profile with native look-at focus. It starts unchecked. When checked, defaults are strong f/1.4 aperture and a camera zoom-distance threshold of 2 units; both are adjustable. Zooming beyond the threshold plus 0.5 units restores the selected profile unchanged, avoiding rapid switching near the boundary. This uses the camera's zoom-distance value, not a measurement to the character's model. Off, activation delay, first-person and all suspension rules take priority.

Install future updates through `/xlplugins` when an updated version is published to this feed. To remove the plugin, uninstall it through `/xlplugins`. You can also remove the custom repository from `/xlsettings` afterward.

## Troubleshooting

If there is no blur, check the status in `/nativedof`. A mismatched executable is unsupported. Report the plugin version, status, render/scene counters, blur-pass values, and relevant NativeDof log lines. Avoid uploading entire logs containing unrelated personal data.

The plugin temporarily overrides native DoF settings across the scene-render operation and restores them afterward. Because it hooks native game functions, crashes or rendering issues are possible.

## Build

Install the .NET 10 SDK and obtain compatible Dalamud API 15 reference libraries through your Dalamud installation. The SDK, Dalamud libraries, game files and disassembly files are not included here.

In PowerShell, run:

```powershell
.\build.ps1 -DalamudLibPath 'C:\path\to\Dalamud\Hooks\15.0.3.9'
```

`-Dotnet` optionally specifies a dotnet executable. The script builds the plugin, runs synthetic-memory checks, and packages it under `release`. Tests cover native layout, input bounds, resource checks and restoration, including exception handling; they do not replace in-game testing.

## Maintaining the repository feed

### Dalamud release monitoring with GitHub Actions

The **Dalamud release maintenance** workflow checks stable Dalamud release metadata every **8 hours** (00:07, 08:07 and 16:07 UTC) using a lightweight GitHub-hosted Linux job. The Windows maintenance job runs only when a new stable revision is available, a compatibility blocker needs reporting, a feed update is unfinished, or a validation-only run is requested. Unchanged compatible releases skip the Windows job entirely. GitHub may delay scheduled checks; this is release polling rather than an immediate upstream webhook. It can also be started from the repository's **Actions** tab. It does not require the author's PC or the Codex app. Select **validate_only** for a build/check run without publishing or opening issues.

The workflow checks the official stable Dalamud metadata, including whether that release supports the current game patch. For a newer compatible Dalamud revision on the same verified game build and API generation, it downloads the references, checks the native structure offsets, builds and runs the tests, increments the plugin version, pushes the source, publishes an experimental GitHub release, verifies the downloaded package, and then updates `repo.json`. It uses GitHub's built-in token; no personal token or AI API key is required. It does not create a release when nothing has changed. Builds that pass the automated checks publish without waiting for manual in-game testing; runtime issues can be addressed afterward.

**Game-patch updates still require native verification.** GitHub-hosted runners do not have a licensed FFXIV installation or a new executable to analyze. This workflow does not run a coding agent or automatically reverse-engineer new game patches. A changed game build, API/runtime generation, native layout, or failed build opens or updates a maintenance issue and leaves the existing feed intact. The exact executable hash check is never bypassed. Automated rebuild releases explicitly state that they have not been visually tested in-game.

`maintenance.json` records the last verified Dalamud and game versions. When manually verifying a new game patch, update that file alongside the native addresses, executable hash, manifest and documentation. Repository rules must permit this workflow's token to push to `main` and create releases/issues. Conflicting remote changes stop publication rather than being overwritten. If a run stops after publishing source or a release, rerun it to finish the feed update before starting another version.

GitHub may delay scheduled runs and disables schedules in public repositories after 60 days without repository activity. Monitor the Actions tab and re-enable the schedule if GitHub disables it.

### Feed format

The root `repo.json` lists the plugin for Dalamud. Its install and update URLs point to the `v0.1.8` release's `NativeDof-0.1.8.zip`. The ZIP must contain `NativeDof.dll` and `NativeDof.json` at its root.

For each future release, upload the new ZIP first, then update the feed's `AssemblyVersion`, download URLs, `LastUpdate` (Unix seconds), and any changed compatibility metadata. Keep `InternalName` as `NativeDof`; the feed's version and API level must match the packaged plugin manifest. Publishing a GitHub release alone does not update this feed. The experimental status is described in the listing; GitHub's pre-release flag does not require users to enable Dalamud testing plugins.

## Acknowledgements

[Dalamud](https://github.com/goatcorp/Dalamud) provides the plugin framework and hook management. [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) provides native camera definitions and informed the DoF parameter layout. Dependencies are supplied by Dalamud at runtime.
