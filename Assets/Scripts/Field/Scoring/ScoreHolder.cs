using TMPro;
using UnityEngine;

namespace Field.Scoring
{
    public class ScoreHolder : MonoBehaviour
    {
        public static int BlueScore;
        public static int RedScore;

        private TextMeshProUGUI _redScoreDisplay;
        private TextMeshProUGUI _blueScoreDisplay;
        // Start is called before the first frame update
        void Start()
        {
            BlueScore = 0;
            RedScore = 0;

            var displayBlue = GameObject.Find("BlueScoreDisplay");
            if (displayBlue != null)
            {
                _blueScoreDisplay = displayBlue.GetComponent<TextMeshProUGUI>();
            }

            var displayRed = GameObject.Find("RedScoreDisplay");
            if (displayRed != null)
            {
                _redScoreDisplay = displayRed.GetComponent<TextMeshProUGUI>();
            }
        }

        void Update()
        {
            if (_blueScoreDisplay != null)
            {
                _blueScoreDisplay.text = BlueScore.ToString();
            }

            if (_redScoreDisplay != null)
            {
                _redScoreDisplay.text = RedScore.ToString();
            }
        }
    }
}
