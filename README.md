# CloSim

CloSim is a Unity-based FIRST Robotics Competition simulator built from [MoSimBuilder](https://github.com/masonmm3/MoSimBuilder). It keeps the MoSimBuilder robot-building workflow, then adds CloSim-specific match flow, local multiplayer, robot selection, game setup, human-player behavior, and season-specific field integration.

CloSim currently supports two FRC-style game environments:

- **Rebuilt**
- **Reefscape**

The project is intended for local simulation, robot testing, driver practice, and importing compatible Builder Bot / MoSimBuilder robots into CloSim match scenes.

## Built from MoSimBuilder

CloSim is derived from MoSimBuilder by Mason Morgan / Cascade Studios. MoSimBuilder provides the base robot-building framework, robot mechanisms, drivetrain systems, input architecture, and Unity project structure.

CloSim builds on that foundation with:

- Game-specific match scenes
- Main menu and game selection flow
- Runtime game setup menus
- Local multiplayer support
- Split-screen camera management
- Robot-selection UI
- Per-player control preferences
- Human-player systems
- Rebuilt and Reefscape-specific integration

For general robot-building concepts, use the MoSimBuilder documentation. For importing a finished Builder Bot into CloSim, use the CloSim modding documentation.

## Major features

### Main menu and game selection

CloSim includes a main menu with game selection, settings, credits, scene transitions, controller navigation support, and startup/splash behavior.

From the menu, players can select a supported game scene, currently Rebuilt or Reefscape. Game selection data is passed into the match scene so the selected game can initialize with the proper settings.

### Match setup menu

Each match scene has an in-game setup menu for configuring the match before play. The menu can pause runtime robot input, reset the field, apply new match settings, and then resume play.

The setup menu supports:

- Game mode selection
- Robot selection
- Spawn-position selection
- Alliance selection when applicable
- Human-player mode selection
- Camera mode selection
- Driver-station camera selection
- Vanity bumper toggle when available
- Ready states for required players
- Back-to-main-menu flow

### Supported play modes

CloSim supports local play modes for one to four players:

- **Singleplayer**
- **Multiplayer: 2v0**
- **Multiplayer: 1v1**
- **Multiplayer: 3v0**
- **Multiplayer: 2v2**

In same-alliance modes, players can be placed on the selected alliance. In versus modes, players are split between blue and red alliances.

### Local multiplayer and input devices

CloSim uses Unity's Input System for local multiplayer. Robots are spawned from selected prefabs and paired to keyboard or gamepad devices at runtime.

Supported input setups include:

- Singleplayer with keyboard
- Singleplayer with gamepad
- Multiplayer with keyboard and gamepad
- Multiplayer with multiple gamepads
- Up to four local player slots, depending on selected play mode

Player control preferences are saved through `PlayerPrefs`, including:

- Preferred input device per player
- Preferred gamepad index per player
- Binding overrides per player

### Settings menu

CloSim includes a settings menu for display and control configuration.

Display settings include:

- Frame-rate cap
- Resolution
- Window mode
- Graphics quality

Control settings include:

- Player selection
- Keyboard/gamepad preference
- Gamepad index selection
- Per-command binding presets
- Per-player reset/default behavior

### Robot selection UI

CloSim builds the robot-selection grid from robot prefabs stored in game-specific `Resources` folders. The robot metadata is configured through the `RobotIdentity` component. This controls team number, display name override, team icon, and robot preview image.

### Camera modes

Players can select camera behavior during match setup.

Supported camera modes include:

- Third Person
- First Person
- Driver Station

CloSim also configures split-screen viewports automatically based on the selected play mode. In four-player modes, cameras are arranged into four quadrants. In some modes, an additional field camera can be used when appropriate.

### Human players

CloSim includes selectable human-player behavior for certain games.

Current human-player modes in:

- **Certified Bucket**
- **Certified Dumper**

Human-player behavior can be assigned to the proper alliance/player slot at runtime. Dumper-style human-player objects can be controlled through the owning robot's input when the selected game supports it.

### Rebuilt-specific systems

Rebuilt support includes Rebuilt field logic, match integration, game-piece behavior, human-player behavior, launch-zone penalty checks, aim-region support, and Rebuilt-specific robot commands.

Rebuilt robot imports may require:

- `LaunchZonePenalty` setup
- Correct bumper collider root assignment
- Hub/passing aim-region configuration
- Rebuilt command remapping, such as `Shoot`, `Intake`, `PassLeft`, `PassRight`, `Hub`, `RobotSpecial`, and `HumanPlayerDump`

### Reefscape-specific systems

Reefscape support includes Reefscape field logic, robot command mapping, and Reefscape-specific mechanisms such as climbing and scoring controls.

Reefscape robot imports may require:

- `ClimberComponent` placement on the climber root
- Reefscape command remapping, such as `AutoAlign`, `L1`, `L2`, `L3`, `L4`, `Barge`, `AlgaeHigh`, `AlgaeLow`, `AlgaeHold`, and `Climb`

### Aim regions

CloSim supports manually placed aim regions. Aim regions can be associated with:

- Blue alliance
- Red alliance
- Neutral zones

Auto-aim and mechanism-aim components can require the robot bumper root or another reference transform to be inside an allowed region before aiming activates. This is used for alliance-specific aiming, hub targeting, and optional passing restrictions.

## Online Multiplayer (Rooms)

> Status: in development. This section describes the online multiplayer capability being added on top of the existing local split-screen play. Local/offline play is unchanged.

CloSim is gaining **online multiplayer** using [Mirror](https://github.com/MirrorNetworking/Mirror) (MIT). Any player can **host a server directly from the client** (a player-hosted listen-server) — no dedicated server is required. Other players can find and join through an in-game **Server List**, connect **directly by IP/port**, or play over **LAN**.

Everything happens inside the game. The room system — the Server List, creating a room, joining, the room lobby, and mode selection — is **native in-game UI**. There is no website or browser involved.

### Room and mode matrix

Online rooms hold up to **6 players**, with a maximum of **3 per alliance**. The host picks the mode in the room lobby. Supported versus shapes (asymmetric allowed):

| Blue \ Red | 1     | 2     | 3     |
|------------|-------|-------|-------|
| **1**      | 1v1   | 1v2   | 1v3   |
| **2**      | 2v1   | 2v2   | 2v3   |
| **3**      | 3v1   | 3v2   | 3v3   |

Same-alliance / co-op modes (1v0, 2v0, 3v0) remain available. Remaining capacity can optionally be filled by **spectator** slots.

### Hosting, joining, and browsing

- **Host a room** from the client. Choose **Public** (listed for anyone to find) or **Private** (not listed; shared by IP or over LAN).
- **Join** by picking a room from the in-game **Server List**, by entering a host's **IP and port** directly, or by **LAN discovery** on the same network.
- **Private rooms** are gated by a **join token / password** so only invited players can connect. A public room may also set a token if the host wants a listed-but-locked room.

### Public discovery (AWS master server)

Public rooms register with a lightweight **master server** (a backend directory the game uses to list public rooms). The master server is a backend service only — its sole client is the game itself; there is no web page to visit.

The master-server hosting endpoint is **user-supplied**: it is left blank in the project and must be configured by whoever runs the directory. Public browsing and hosting are unavailable until that endpoint is provided. Direct-IP and LAN play work without it.

For contributor-facing details (architecture, interface contracts, phased plan), see `CLAUDE.md`, `AGENTS.md`, and `Documentation/online/`.

## Documentation

```text
Documentation/Importing_Builder_Bots_to_CloSim.md
Documentation/online/architecture.md
Documentation/online/agents/
```

## Credits

CloSim is derived from MoSimBuilder by Mason Morgan / Cascade Studios.

Original project:

```text
https://github.com/masonmm3/MoSimBuilder
```

CloSim modifications and game-specific additions are maintained as part of the CloSim project.

## License

This project is derived from MoSimBuilder. Follow the license terms of the original project (e.g. Available upon request).
