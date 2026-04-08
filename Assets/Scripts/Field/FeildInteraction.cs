using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FeildInteraction : MonoBehaviour
{
    protected GamePiece gamePiece;
    
    public GamePiece GetGamePiece() { return gamePiece; }
    public void SetGamePiece(GamePiece gamePiece) { this.gamePiece = gamePiece; }
}
