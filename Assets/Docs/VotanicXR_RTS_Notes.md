# VotanicXR RTS Notes (Unity)

Date: 2026-07-02

## Goal
Implement an RTS-like flow with VotanicXR wand:
1. Aim and click a unit to select it.
2. Aim and click ground to issue move order.
3. Auto-deselect after one move order (optimized for commanding multiple units).

## What Was Implemented

### Scripts Added
- Assets/Scripts/RtsUnitMotor.cs
- Assets/Scripts/VotanicWandRtsCommander.cs
- Assets/Scripts/RtsUnitHighlight.cs

### Core Behavior
- `RtsUnitMotor`: simple move-to-point and rotate-toward-motion.
- `VotanicWandRtsCommander`:
  - Uses Votanic command input via `vGear.Cmd.Received(...)`.
  - Defaults to `Grab` command to avoid common teleport conflict on `Trigger`.
  - Desktop fallback click input uses `Mouse0` by default.
  - Hover detection and selection state management.
  - Auto-deselect after successful move order.
- `RtsUnitHighlight`:
  - Hover state -> green glow.
  - Selected state -> red glow.
  - Uses material property blocks (`_EmissionColor`, `_OutlineColor`, `_OutlineWidth`).

## VotanicXR Findings

### Important Things That Are True
- Keep the Votanic controller system enabled.
  - Disabling vGear controller can leave visuals active while command input stops working.
- Command-based input is reliable in samples.
  - Existing tutorials use `vGear.Cmd.Received("Trigger")`, `vGear.Cmd.Received("Grab")`, etc.
- Votanic sample callbacks include object interaction events:
  - `OnWandSelect`, `OnWandDeselect`, `OnWandPress`, `OnWandRelease` patterns are supported in template scripts.

### Important Things That Are Not Obvious
- Visible ray does not guarantee active command routing.
  - A ray can still render even when input command processing is disabled/misaligned.
- `Trigger` often overlaps with teleport workflows.
  - For RTS commanding, use a separate command (`Grab`) to reduce tool conflict.
- Desktop testing can drift from VR mappings.
  - A dedicated desktop fallback key/mouse path helps isolate mapping issues.

## Where Important VotanicXR References Are in This Project
- Input/tutorial examples:
  - Assets/Votanic/VotanicXR_Tutorial 2020/Tutorial02_InputSystem/Sample
- Locomotion/teleport examples:
  - Assets/Votanic/VotanicXR_Tutorial 2020/Tutorial03_Locomotion/Sample
- Interaction examples:
  - Assets/Votanic/VotanicXR_Tutorial 2020/Tutorial04_Interaction/Sample
- Utility template classes (with commented callback signatures):
  - Assets/Votanic/VotanicXR/vGear/Scripts/vGearInteractablesTemp.cs
  - Assets/Votanic/VotanicXR/vCast/Scripts/vCastInteractablesTemp.cs

## Editor Setup Reference

### Layers
- `RTS_Unit`: assign to all commandable units.
- `RTS_Ground`: assign to clickable ground/plane.

### Object Components
- Each unit:
  - Combat footprint `Collider` (keep size = true regiment footprint; used for combat overlaps)
  - Auto child `SelectionVolume` (tall trigger on `RTS_Unit`, easier wand pick in 3D)
  - Auto child `MovementBounds` (wall casts only)
  - `RtsUnitMotor` with `Is Command Unit` left enabled
  - `RtsUnitHighlight` (outline uses SelectionVolume when present)
  - `TroopCombat` with `Project Troop Visuals To Ground` enabled so soldiers sit on `RTS_Ground` slopes
- Ground:
  - Collider on `RTS_Ground` (mesh/terrain OK; movement still treats the field as a flat plane)
- Commander object (active in scene):
  - `VotanicWandRtsCommander`
  - Set `Wand Origin` to intended pointing transform.
  - `Selectable Layers` = RTS_Unit
  - `Ground Layers` = RTS_Ground

### 3D Battlefield Notes
- Author units high in Y (e.g. y=100) if convenient — on `Start` / match reset each `TroopCombat` snaps its **root** onto `RTS_Ground`, so footprint, selection, and movement bounds come down together.
- While moving, root Y keeps sticking to ground under the regiment (XZ movement still ignores elevation cost).
- Individual troop meshes can additionally snap to the ground under their formation slot for slopes.
- Path aim hits use ground XZ but force Y to the commanding unit's plane.

### Recommended Commander Defaults
- `Issue Command Name`: `Grab`
- `Enable Desktop Fallback`: true
- `Desktop Key A`: Mouse0
- `Desktop Key B`: None
- `Auto Deselect After Move`: true
- `Clear Selection On Miss Click`: true

## Known Risks / Limitations
- Glow visibility depends on shader support for emission or outline properties.
- Current movement is direct line movement (no obstacle avoidance).
- No group selection yet (single selected unit at a time by design).
- If a wall or prop has `RtsUnitMotor` for some reason, set `Is Command Unit` to false so it cannot be selected.

## Suggested Next Improvements
1. Add click-and-drag marquee selection for multi-unit selection.
2. Add NavMeshAgent mode for obstacle-aware movement.
3. Add explicit RTS/Teleport mode switch to toggle tool visibility and command maps.
4. Add selected-unit marker ring prefab for clearer readability.
