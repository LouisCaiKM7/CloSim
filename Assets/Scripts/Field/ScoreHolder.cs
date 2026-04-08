using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ScoreHolder : MonoBehaviour
{
    public static int BlueScore;
    public static int RedScore;
    [SerializeField] int blueScore;
    
    private TextMeshProUGUI redScoreDisplay;
    private TextMeshProUGUI blueScoreDisplay;
    // Start is called before the first frame update
    void Start()
    {
        BlueScore = 0;
        RedScore = 0;

        var dispB = GameObject.Find("BlueScoreDisplay");
        if (dispB != null)
        {
            blueScoreDisplay = dispB.GetComponent<TextMeshProUGUI>();
        }

        var dispR = GameObject.Find("RedScoreDisplay");
        if (dispR != null)
        {
            redScoreDisplay = dispR.GetComponent<TextMeshProUGUI>();
        }
    }

    void Update()
    {
        blueScore = BlueScore;

        if (blueScoreDisplay != null)
        {
            blueScoreDisplay.text = BlueScore.ToString();
        }

        if (redScoreDisplay != null)
        {
            redScoreDisplay.text = RedScore.ToString();
        }
    }
}
