using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

public class SpawnRobots : MonoBehaviour
{
    [SerializeField] private GameObject robotPrefabA;
    [SerializeField] private GameObject robotPrefabB;
    [SerializeField] private Transform spawnA;
    [SerializeField] private Transform spawnB;

    private GameObject robotA;
    private GameObject robotB;

    void Start()
    {
        var pads = Gamepad.all;

        if (pads.Count < 2)
        {
            Debug.LogError("Need two gamepads connected.");
            return;
        }

        robotA = Instantiate(robotPrefabA, spawnA.position, spawnA.rotation);
        robotB = Instantiate(robotPrefabB, spawnB.position, spawnB.rotation);

        SetupPlayer(robotA, pads[0]);
        SetupPlayer(robotB, pads[1]);
    }

    void SetupPlayer(GameObject robot, Gamepad gamepad)
    {
        var playerInput = robot.GetComponent<PlayerInput>();
        if (playerInput == null)
        {
            Debug.LogError($"Robot {robot.name} is missing PlayerInput.");
            return;
        }

        playerInput.user.UnpairDevices();
        
        InputUser.PerformPairingWithDevice(gamepad, playerInput.user);

        InputUser.PerformPairingWithDevice(Keyboard.current, playerInput.user);

        playerInput.ActivateInput();
        playerInput.SwitchCurrentActionMap("Robot");
    }
}