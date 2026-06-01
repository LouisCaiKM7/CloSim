using UnityEngine;

public class RobotIdentity : MonoBehaviour
{
    [Header("Broadcast UI")]
    public int teamNumber = 0;

    [Tooltip("Used if teamNumber is 0.")]
    public string displayNameOverride = "";

    [Tooltip("Optional icon shown next to the team number/name in the HUD.")]
    public Sprite teamIcon;

    public string GetBroadcastLabel()
    {
        if (teamNumber > 0)
            return teamNumber.ToString();

        if (!string.IsNullOrWhiteSpace(displayNameOverride))
            return displayNameOverride.Trim();

        return gameObject.name
            .Replace("_P1", "")
            .Replace("_P2", "")
            .Replace("(Clone)", "")
            .Trim();
    }
}