using Field.Scoring;
using TMPro;
using UnityEngine;

namespace UI.HUD
{
    public class ReefscapeHudUI : MonoBehaviour
    {
        [Header("Blue Texts")]
        [SerializeField] private TMP_Text blueCoralText;
        [SerializeField] private TMP_Text blueAlgaeText;

        [Header("Red Texts")]
        [SerializeField] private TMP_Text redCoralText;
        [SerializeField] private TMP_Text redAlgaeText;

        private void Awake()
        {
            if (blueCoralText == null)
            {
                GameObject obj = GameObject.Find("BlueCoralText");
                if (obj != null)
                    blueCoralText = obj.GetComponent<TMP_Text>();
            }

            if (blueAlgaeText == null)
            {
                GameObject obj = GameObject.Find("BlueAlgaeText");
                if (obj != null)
                    blueAlgaeText = obj.GetComponent<TMP_Text>();
            }

            if (redCoralText == null)
            {
                GameObject obj = GameObject.Find("RedCoralText");
                if (obj != null)
                    redCoralText = obj.GetComponent<TMP_Text>();
            }

            if (redAlgaeText == null)
            {
                GameObject obj = GameObject.Find("RedAlgaeText");
                if (obj != null)
                    redAlgaeText = obj.GetComponent<TMP_Text>();
            }
        }

        private void Update()
        {
            if (blueCoralText != null)
                blueCoralText.text = FieldScorer.BlueCoral.ToString();

            if (blueAlgaeText != null)
                blueAlgaeText.text = FieldScorer.BlueAlgae.ToString();

            if (redCoralText != null)
                redCoralText.text = FieldScorer.RedCoral.ToString();

            if (redAlgaeText != null)
                redAlgaeText.text = FieldScorer.RedAlgae.ToString();
        }
    }
}