# Native DoF

Experimental Dalamud plugin that enables FFXIV's native updated depth of field during gameplay.

## Compatibility

Version **0.1.3**, **Dalamud API 15**, Windows x64. Built against Dalamud 15.0.3.5 and .NET 10.

This prototype supports only game build **2026.09.15.0000.0000**, with `ffxiv_dx11.exe` SHA256:

`5BBC501DD5C7F22FD61A11D08C25356041D878DB7CD83203ADAE393E4DFACC44`

Other executables are rejected before native hooks are created. Game updates require fresh analysis and a plugin update. Do not bypass that check.

Gameplay DoF was visually confirmed in 0.1.2. Version 0.1.3 adds first-person suspension; that transition has not yet been confirmed in-game. This remains a prototype. Performance, graphics-mod compatibility, and all transition cases have not been systematically tested.

## Install

1. Download `NativeDof-0.1.3.zip` from this repository's Releases page.
2. Extract it into a permanent folder outside the game installation, such as `Documents\NativeDof`.
3. In FFXIV, open `/xlsettings` → **Experimental** → **Dev Plugin Locations**. Add the full path to the extracted `NativeDof.dll`, then save.
4. Enable **Native DoF Prototype** in Dalamud's developer plugin list.
5. Run `/nativedof` and enable gameplay depth of field.

The effect starts off each time the plugin loads. This release uses developer-plugin loading; there is no custom repository feed or automatic update mechanism yet. Do not put the DLL in the game folder.

## Controls

- `/nativedof`: open controls.
- `/nativedof on`: enable the effect.
- `/nativedof off`: disable the effect.
- `/nativedof status`: open controls and write diagnostics to the Dalamud log.

Use native camera focus to focus at the camera's look-at distance, or set a manual focus distance. Lower aperture values produce more blur; try f/2.8 while standing outdoors in front of a distant background.

DoF suspends in first-person and resumes in third-person if enabled. It also suspends during cutscenes, GPose, loading and logout. Combat suspension is enabled by default and can be turned off. Existing native cutscene and GPose settings remain under game control.

For an update, disable the plugin, replace the extracted files, and enable it again. To remove it, disable it and remove its Dev Plugin Location.

## Troubleshooting

If there is no blur, check the status in `/nativedof`. A mismatched executable is unsupported. Report the plugin version, status, render/scene counters, blur-pass values, and relevant NativeDof log lines. Avoid uploading entire logs containing unrelated personal data.

The plugin temporarily overrides native DoF settings across the scene-render operation and restores them afterward. Because it hooks native game functions, crashes or rendering issues are possible.

## Build

Install the .NET 10 SDK and obtain compatible Dalamud API 15 reference libraries through your Dalamud installation. The SDK, Dalamud libraries, game files and disassembly files are not included here.

In PowerShell, run:

```powershell
.\build.ps1 -DalamudLibPath 'C:\path\to\Dalamud\Hooks\15.0.3.5'
```

`-Dotnet` optionally specifies a dotnet executable. The script builds the plugin, runs synthetic-memory checks, and packages it under `release`. Tests cover native layout, input bounds, resource checks and restoration, including exception handling; they do not replace in-game testing.

## Acknowledgements

[Dalamud](https://github.com/goatcorp/Dalamud) provides the plugin framework and hook management. [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) provides native camera definitions and informed the DoF parameter layout. Dependencies are supplied by Dalamud at runtime.
