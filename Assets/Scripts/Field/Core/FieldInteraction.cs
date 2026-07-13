using Robot.Runtime;
using UnityEngine;

namespace Field.Core
{
    public class FieldInteraction : MonoBehaviour
    {
        protected GamePiece GamePiece;
    
        public GamePiece GetGamePiece() { return GamePiece; }
        public void SetGamePiece(GamePiece gamePiece) { this.GamePiece = gamePiece; }
    }
}
