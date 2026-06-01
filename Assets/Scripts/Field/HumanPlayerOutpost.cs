using UnityEngine;
using Util;

public class HumanPlayerOutpost : MonoBehaviour
{
    [SerializeField] private bool isBlue;
    [SerializeField] private HumanPlayerType type;

    [Tooltip("Optional. If null, this GameObject is toggled.")]
    [SerializeField] private GameObject visualRoot;

    public bool IsBlue => isBlue;
    public HumanPlayerType Type => type;

    public void SetVisible(bool visible)
    {
        GameObject target = visualRoot != null ? visualRoot : gameObject;

        if (target.activeSelf != visible)
            target.SetActive(visible);
    }
}