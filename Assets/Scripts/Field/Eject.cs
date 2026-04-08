using System.Collections;
using System.Collections.Generic;
using BuilderLib;
using UnityEditor;
using UnityEngine;
using Util;

[ExecuteAlways]
public class Eject : FeildInteraction
{
    [SerializeField] private NodeAction ejectType;

    // Update is called once per frame
    void FixedUpdate()
    {
        if (gamePiece)
        {
            GamePieceManager.changeParent(gamePiece, transform);
            GamePieceManager.ReleaseToWorld(gamePiece, ejectType);
            StartCoroutine(GamePieceManager.enableColliders(gamePiece));
            gamePiece = null;
        }
    }

    void Update()
    {
        #if UNITY_EDITOR
        if (EditorApplication.isPlaying) return;
        #endif
        ejectType.Type = NodeType.Outake;
    }
}
