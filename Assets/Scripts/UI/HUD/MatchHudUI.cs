using Core;
using Robot.Runtime;
using TMPro;
using UI.RobotSelection;
using UnityEngine;
using UnityEngine.UI;
using PlayMode = Core.PlayMode;

namespace UI.HUD
{
    public class MatchHudUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LoadMatch loadMatch;

        [Header("Blue Team Slots")]
        [SerializeField] private TMP_Text[] blueTeamTexts;
        [SerializeField] private Image[] blueTeamIcons;

        [Header("Red Team Slots")]
        [SerializeField] private TMP_Text[] redTeamTexts;
        [SerializeField] private Image[] redTeamIcons;

        [Header("Display")]
        [SerializeField] private string emptySlotText = "";
        [SerializeField] private bool refreshContinuously = true;

        private GameObject[] _lastRobots = System.Array.Empty<GameObject>();
        private PlayMode _lastPlayMode;
        private bool _lastUsesBlueAlliance;

        private void Awake()
        {
            if (loadMatch == null)
                loadMatch = FindFirstObjectByType<LoadMatch>();
        }

        private void OnEnable()
        {
            ForceRefresh();
        }

        private void Update()
        {
            if (!refreshContinuously)
                return;

            if (loadMatch == null)
            {
                loadMatch = FindFirstObjectByType<LoadMatch>();
                if (loadMatch == null)
                    return;
            }

            GameObject[] robots = loadMatch.GetLoadedRobots();
            PlayMode playMode = loadMatch.GetPlayMode();
            bool usesBlueAlliance = loadMatch.UsesBlueAlliance();

            bool changed =
                RobotsChanged(robots) ||
                playMode != _lastPlayMode ||
                usesBlueAlliance != _lastUsesBlueAlliance;

            if (!changed)
                return;

            CacheLastState(robots, playMode, usesBlueAlliance);
            RefreshTeamNumbers();
        }

        private void ForceRefresh()
        {
            _lastRobots = System.Array.Empty<GameObject>();
            RefreshTeamNumbers();
        }

        private void RefreshTeamNumbers()
        {
            ClearSlots(blueTeamTexts, blueTeamIcons);
            ClearSlots(redTeamTexts, redTeamIcons);

            if (loadMatch == null)
                loadMatch = FindFirstObjectByType<LoadMatch>();

            if (loadMatch == null)
                return;

            GameObject[] robots = loadMatch.GetLoadedRobots();

            if (robots == null)
                return;

            foreach (var t in robots)
                AddRobotToHud(t);
        }

        private bool RobotsChanged(GameObject[] robots)
        {
            robots ??= System.Array.Empty<GameObject>();

            if (_lastRobots == null || _lastRobots.Length != robots.Length)
                return true;

            for (int i = 0; i < robots.Length; i++)
            {
                if (robots[i] != _lastRobots[i])
                    return true;
            }

            return false;
        }

        private void CacheLastState(GameObject[] robots, PlayMode playMode, bool usesBlueAlliance)
        {
            robots ??= System.Array.Empty<GameObject>();

            _lastRobots = new GameObject[robots.Length];

            for (int i = 0; i < robots.Length; i++)
                _lastRobots[i] = robots[i];

            _lastPlayMode = playMode;
            _lastUsesBlueAlliance = usesBlueAlliance;
        }

        private void AddRobotToHud(GameObject robot)
        {
            if (robot == null)
                return;

            RobotIdentity identity = robot.GetComponentInChildren<RobotIdentity>(true);

            string label = GetRobotLabel(robot, identity);
            Sprite icon = identity != null ? identity.teamIcon : null;

            bool robotIsRed = IsRobotRed(robot);

            TMP_Text[] targetTexts = robotIsRed ? redTeamTexts : blueTeamTexts;
            Image[] targetIcons = robotIsRed ? redTeamIcons : blueTeamIcons;

            SetFirstEmptySlot(targetTexts, targetIcons, label, icon);
        }

        private string GetRobotLabel(GameObject robot, RobotIdentity identity)
        {
            if (identity != null)
                return identity.GetBroadcastLabel();

            return robot.name
                .Replace("_P1", "")
                .Replace("_P2", "")
                .Replace("_P3", "")
                .Replace("_P4", "")
                .Replace("(Clone)", "")
                .Trim();
        }

        private bool IsRobotRed(GameObject robot)
        {
            SwerveController swerve = robot.GetComponentInChildren<SwerveController>(true);
            return swerve != null && swerve.isRed;
        }

        private void SetFirstEmptySlot(TMP_Text[] texts, Image[] icons, string value, Sprite icon)
        {
            if (texts == null)
                return;

            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];

                if (text == null)
                    continue;

                bool slotIsEmpty =
                    string.IsNullOrWhiteSpace(text.text) ||
                    text.text == emptySlotText ||
                    !text.gameObject.activeSelf;

                if (!slotIsEmpty)
                    continue;

                text.text = value;
                text.gameObject.SetActive(true);

                SetIcon(icons, i, icon);
                return;
            }
        }

        private void SetIcon(Image[] icons, int index, Sprite icon)
        {
            if (icons == null)
                return;

            if (index < 0 || index >= icons.Length)
                return;

            Image image = icons[index];

            if (image == null)
                return;

            if (icon == null)
            {
                image.sprite = null;
                image.gameObject.SetActive(false);
                return;
            }

            image.sprite = icon;
            image.preserveAspect = true;
            image.gameObject.SetActive(true);
        }

        private void ClearSlots(TMP_Text[] texts, Image[] icons)
        {
            if (texts != null)
            {
                foreach (var text in texts)
                {
                    if (text == null)
                        continue;

                    text.text = emptySlotText;
                    text.gameObject.SetActive(false);
                }
            }

            if (icons != null)
            {
                foreach (var icon in icons)
                {
                    if (icon == null)
                        continue;

                    icon.sprite = null;
                    icon.gameObject.SetActive(false);
                }
            }
        }
    }
}