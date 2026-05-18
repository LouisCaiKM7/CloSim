using TMPro;
using UnityEngine;

public class FpsDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text fpsText;

    [Header("Display")]
    [SerializeField] private float refreshInterval = 0.25f;
    [SerializeField] private string prefix = "FPS: ";

    private float _timer;
    private int _frames;

    private void Awake()
    {
        if (fpsText == null)
            fpsText = GetComponent<TMP_Text>();
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        _frames++;

        if (_timer < refreshInterval)
            return;

        float fps = _frames / _timer;

        if (fpsText != null)
            fpsText.text = $"{prefix}{Mathf.RoundToInt(fps)}";

        _timer = 0f;
        _frames = 0;
    }
}