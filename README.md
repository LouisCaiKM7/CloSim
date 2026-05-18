# CloSim

CloSim is a Unity-based simulation project for the 2026 FRC **Rebuilt** game. It is built from MoSimBuilder, so this README focuses on CloSim-specific additions and workflow differences.

For general robot-building setup, use the MoSim / MoSimBuilder documentation as the source of truth. CloSim assumes robots are still created through the MoSimBuilder workflow, then imported into CloSim for Rebuilt match play, local multiplayer, human players, and field/game integration.

## Built from MoSimBuilder

CloSim started from [MoSimBuilder](https://github.com/masonmm3/MoSimBuilder). MoSimBuilder provides the base robot-building framework, robot mechanisms, input framework, and Unity project structure. CloSim adds a match environment and runtime systems on top of that foundation.

## Major CloSim additions

### Rebuilt match menu

CloSim adds an in-game match menu for selecting game mode, camera mode, frame rate, window mode, alliance, robots, spawn positions, human-player mode, controls, and credits. The menu can reset the field after settings are applied and temporarily disables robot input while open.

The match menu supports:

- Singleplayer
- Local multiplayer: 2v0
- Local multiplayer: 1v1
- Blue / red alliance selection when applicable
- Robot selection panels
- Spawn-position selection
- Camera mode selection
- Human-player mode selection
- Frame-rate and window-mode settings

### Local multiplayer

CloSim supports local multiplayer through Unity’s Input System. Robots are spawned from selected prefabs and paired to available input devices at runtime.

Supported input setups:

- Singleplayer with keyboard
- Singleplayer with controller
- Multiplayer with one controller and one keyboard
- Multiplayer with two controllers

For two-robot modes, CloSim spawns two robots and configures split-screen cameras. One robot can be placed on each alliance in 1v1 mode, or both can be placed on the same alliance in 2v0 mode.

### All builder bots

CloSim loads available robot prefabs from `Resources/Robots`, allowing all compatible builder bots to appear in the robot-selection UI. Robot preview sprites can also be loaded from `Resources/RobotPreviews` when available. 

### Shift sounds and shift UI

CloSim includes Rebuilt-specific match timing, match sounds, shift sounds, endgame sounds, and match-end sounds through the FMS controller.

The shift UI displays blue and red shift indicators based on the active shift state. It can show both arrows during auto, transition, and endgame, and hide the indicators when the match is finished.

### Human players

CloSim adds selectable human-player behavior. The runtime state tracks whether the selected human-player type is active for blue and/or red, and whether the dumper is allowed for a given alliance.

The match menu supports two human-player modes:

- Certified Bucket
- Certified Dumper

Outpost dumper HPs can be assigned to a player slot and controlled through the owning robot’s input. The dumper can move fuel to a target transform, wait, and return it back to the playing field.

### Aim regions

CloSim adds manually placed aim regions. Each region is a `BoxCollider` marked as blue alliance, red alliance, or neutral. Auto-aim and mechanism-aim systems can require the robot or another reference transform to be inside an allowed region before aiming activates.

## How to import a MoSimBuilder robot into CloSim

Use the normal MoSimBuilder process to create and export/build your robot first. CloSim does not replace the builder workflow; it adds a Rebuilt match environment around builder-compatible robot prefabs.

### 1. Build or prepare the robot in MoSimBuilder

Create the robot using the MoSimBuilder documentation and workflow. Keep the robot compatible with the standard builder setup, especially the drivetrain, mechanisms, controls, and `PlayerInput` assumptions.

### 2. Add the robot prefab to CloSim

Place the finished robot prefab in:

`Assets/Resources/Robots` 

CloSim scans `Resources/Robots` at runtime and adds every robot prefab it finds to the robot-selection list.

### 3. Add an optional robot preview image

To show a preview image in the match menu, add a sprite with the same name as the robot prefab to:

`Assets/Resources/RobotPreviews`

For example:

`Assets/Resources/Robots/MyRobot.prefab`
`Assets/Resources/RobotPreviews/MyRobot.png`

Be sure to set the .png to be a "Sprite".

If no matching preview sprite exists, CloSim will still load the robot, but the UI may show a placeholder.

### 4. Check input compatibility

CloSim expects robot input to use the configured robot action map used in Builder. The match loader can add or configure PlayerInput at runtime if the robot prefab is missing it, but the project still needs a valid input actions asset assigned in the match loader.

### 5. Test in the match menu

Start CloSim, open the match menu, and select the robot from the robot panel. Choose the desired mode:

Singleplayer
Multiplayer: 2v0
Multiplayer: 1v1

Then select the spawn position, camera mode, alliance settings, human-player mode, and apply the settings. The field will reset using the selected configuration.

# License
This project is derived from MoSimBuilder by Mason Morgan / Cascade Studios.

Original project:
https://github.com/masonmm3/MoSimBuilder

CloSim modifications are made from the 2026 FRC Rebuilt project.
