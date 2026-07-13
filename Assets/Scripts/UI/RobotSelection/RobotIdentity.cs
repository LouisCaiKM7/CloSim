using UnityEngine;

namespace UI.RobotSelection
{
    public class RobotIdentity : MonoBehaviour
    {
        [Header("Broadcast UI")]
        public int teamNumber;

        [Tooltip("Used if teamNumber is 0.")]
        public string displayNameOverride = "";

        [Tooltip("Team icon shown next to the team number/name in the HUD.")]
        public Sprite teamIcon;
    
        [Tooltip("Robot preview shown in robot selection UI. Falls back to teamIcon if empty.")]
        public Sprite robotPreview;

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
    
        public Sprite GetRobotPreview()
        {
            return robotPreview != null ? robotPreview : teamIcon;
        }
    }
}