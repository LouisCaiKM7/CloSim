using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Util;

public class RestartMatch : MonoBehaviour
{
    private LoadMatch _matchLoader;
    private PlayerInput _playerInput;
    private InputAction _resetAction;
    // Start is called before the first frame update
    void Start()
    {
        _matchLoader = Utils.FindParentObjectComponent<LoadMatch>(gameObject);
        _playerInput = gameObject.GetComponent<PlayerInput>();
        _resetAction = _playerInput.actions.FindAction("Reset");
    }

    // Update is called once per frame
    void Update()
    {
        if (_resetAction.triggered)
        {
            _matchLoader.ResetField();
        }
    }
}
