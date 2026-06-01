using TMPro;
using UnityEngine;

public class FuelHudUI : MonoBehaviour
{
    [Header("Existing UI Texts")]
    [SerializeField] private TMP_Text blueFuelText;
    [SerializeField] private TMP_Text redFuelText;

    private void Awake()
    {
        if (blueFuelText == null)
        {
            GameObject blueObj = GameObject.Find("BlueFuelText");
            if (blueObj != null)
                blueFuelText = blueObj.GetComponent<TMP_Text>();
        }

        if (redFuelText == null)
        {
            GameObject redObj = GameObject.Find("RedFuelText");
            if (redObj != null)
                redFuelText = redObj.GetComponent<TMP_Text>();
        }
    }

    private void Update()
    {
        if (blueFuelText != null)
            blueFuelText.text = FieldScorer.BlueFuel.ToString();

        if (redFuelText != null)
            redFuelText.text = FieldScorer.RedFuel.ToString();
    }
}