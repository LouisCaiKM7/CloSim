using Core;
using Robot.Builders;
using Robot.Runtime;
using UnityEditor;
using UnityEngine;

namespace Field.Core
{
    [ExecuteAlways]
    public class Eject : FieldInteraction
    {
        [SerializeField] private NodeAction ejectType;

        // Update is called once per frame
        void FixedUpdate()
        {
            if (GamePiece)
            {
                GamePieceManager.ChangeParent(GamePiece, transform);
                GamePieceManager.ReleaseToWorld(GamePiece, ejectType);
                StartCoroutine(GamePieceManager.EnableColliders(GamePiece));
                GamePiece = null;
            }
        }

        void Update()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying) return;
#endif
            ejectType.type = NodeType.OutTake;
        }
    }
}
