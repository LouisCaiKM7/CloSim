using UnityEngine;

public class RebuiltShiftUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject blueShiftArrow; // LeftShift
    [SerializeField] private GameObject redShiftArrow;  // RightShift

    [Header("Display Rules")]
    [SerializeField] private bool showBothDuringAuto = true;
    [SerializeField] private bool showBothDuringTransition = true;
    [SerializeField] private bool showBothDuringEndgame = true;
    [SerializeField] private bool hideWhenFinished = true;

    private void Awake()
    {
        ResolveReferences();
        SetArrows(false, false);
    }

    private void Update()
    {
        ResolveReferences();
        UpdateShiftDisplay();
    }

    private void ResolveReferences()
    {
        if (blueShiftArrow == null)
            blueShiftArrow = FindChildIncludingInactive("LeftShift");

        if (redShiftArrow == null)
            redShiftArrow = FindChildIncludingInactive("RightShift");
    }

    private GameObject FindChildIncludingInactive(string objectName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child.name == objectName)
                return child.gameObject;
        }

        return null;
    }

    private void UpdateShiftDisplay()
    {
        if (hideWhenFinished && FMS.MatchState == MatchState.finished)
        {
            SetArrows(false, false);
            return;
        }

        switch (RebuiltShifts.ActiveShift)
        {
            case RebuiltShifts.CurrentShift.Auto:
                SetArrows(showBothDuringAuto, showBothDuringAuto);
                break;

            case RebuiltShifts.CurrentShift.Transition:
                SetArrows(showBothDuringTransition, showBothDuringTransition);
                break;

            case RebuiltShifts.CurrentShift.Shift1:
            case RebuiltShifts.CurrentShift.Shift3:
                SetArrows(
                    RebuiltShifts.BlueOwnsOddShifts,
                    !RebuiltShifts.BlueOwnsOddShifts
                );
                break;

            case RebuiltShifts.CurrentShift.Shift2:
            case RebuiltShifts.CurrentShift.Shift4:
                SetArrows(
                    !RebuiltShifts.BlueOwnsOddShifts,
                    RebuiltShifts.BlueOwnsOddShifts
                );
                break;

            case RebuiltShifts.CurrentShift.EndGame:
                SetArrows(showBothDuringEndgame, showBothDuringEndgame);
                break;

            default:
                SetArrows(false, false);
                break;
        }
    }

    private void SetArrows(bool blueActive, bool redActive)
    {
        if (blueShiftArrow != null)
            blueShiftArrow.SetActive(blueActive);

        if (redShiftArrow != null)
            redShiftArrow.SetActive(redActive);
    }
}