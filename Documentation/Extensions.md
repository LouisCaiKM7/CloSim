# Extensions

Acknowledging the limitations of the Builder system, the Beta implements an `Extensions System` to increase the possible complexity

Currently, the Extensions System contains the following:
* `Auto Align`
* `Spawn Piece Target`
* `Attach At Startup`
* `Auto Aim`
* `Point At Target`
* `Interpolate Node`
* `Symmetry Tool`

### Auto Align
 * This script should be added to the same object as build frame, and has settings to align, it is NOT season specific and as such uses a simple set of options

### Spawn Piece Target
 * Can be added to any object and simply serves as a reference point for game piece spawning

### Attach at Startup
* Attach at startup is used for advanced Physics implementations. To minimize failures, builder generates all physics elements at runtime, so this script is required to attach custom rigidbody elements to a robot

### Auto Aim
* This script allows you to automatically aim a robots frame at a point.
* It automatically pools default points for each field it supports (Rebuilt only right now)
* There are 4 target modes:
    * `Closest` which points at the closest available target
    * `Furtherst` which points at the furthest available target
    * `Preset` which always points at the same fixed point
    * `Custom` which points at the closest target in the `extra targets` field
* There are 4 target condition modes:
     * `always` which always points at the target
     * `at setpoint` which allows you to specify a subsystem and setpoint name to activate the action
     * `when pressing` which aims when a button is held
     * `within range` which aims when within a certain distance of the target
* Similar to mechanisms it supports custom PID values
* NOTE: it always uses the `forward` or z axis as its target for pointing

### Point at Target
* This script allows you to automatically aim a mechanism at a point.
* It automatically pools default points for each field it supports (Rebuilt only right now)
* There are 4 target modes:
    * `Closest` which points at the closest available target
    * `Furtherst` which points at the furthest available target
    * `Preset` which always points at the same fixed point
    * `Custom` which points at the closest target in the `extra targets` field
* There are two target condition modes:
    * `Always` which will always run the scripts behaviour
    * `At Setpoint` which checks for when the attached mechanism is at a setpoint and toggles behaviour based on that
* There are two methods for calculating the target angle/distance
    * `Point at offset` automatically calculates the angle to point at the point in space. Has height and angle offsets for tuning (Elevators are not supported)
    * `Interpolation` which uses an interpolation table to identify a target value based on distance
 * NOTE: a mechanism may have more than ONE of these to stack unique behaviours

### Interpolate Node
* This script allows you to automatically change the output speed of an outake on a game piece node based on an interpolation table
* It automatically pools default points for each field it supports (Rebuilt only right now)
* There are 4 target modes:
    * `Closest` which points at the closest available target
    * `Furtherst` which points at the furthest available target
    * `Preset` which always points at the same fixed point
    * `Custom` which points at the closest target in the `extra targets` field
* There are two target condition modes:
    * `Always` which will always run the scripts behaviour
    * `At Setpoint` which checks for when the attached mechanism is at a setpoint and toggles behaviour based on that
      
 ### Symmetry Tool
 * this allows building half of a robot and ensuring the other half is symmetrical
 * It creates two children, `toMirror` and `mirrored`
 * everything childed to `toMiror` is automatically copied and mirrored to `mirrored`
 * NOTE: the tool is very early development and is easy to break. To fix delete the `mirrored` object and click the `FORCE UPDATE MIRROR` button.

### Future Extensions
There currently is no plan for more `Extensions` however the system will be getting split into an `Events System` and `Extensions System` so keep an eye out for whats to come.


# [Further Reading](FurtherReading.md)
