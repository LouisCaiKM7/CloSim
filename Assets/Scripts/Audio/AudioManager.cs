using UnityEngine;

namespace Audio
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }
        
        [Header("UI SFX")]
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip confirmClip;
        [SerializeField] private AudioClip backClip;
        [SerializeField] private AudioClip errorClip;
        [SerializeField] private AudioClip transitionClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.8f;

        private const string SfxVolumePrefKey = "Audio_SfxVolume";

        private AudioSource _musicSource;
        private AudioSource _sfxSource;
        private Coroutine _musicFadeRoutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            sfxVolume = PlayerPrefs.GetFloat(SfxVolumePrefKey, sfxVolume);

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.volume = 0f;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
        }

        public void SetSfxVolume(float value)
        {
            sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumePrefKey, sfxVolume);
        }

        public void PlayHover() => PlaySfx(hoverClip);
        public void PlayConfirm() => PlaySfx(confirmClip);
        public void PlayBack() => PlaySfx(backClip);
        public void PlayError() => PlaySfx(errorClip);
        public void PlayTransition() => PlaySfx(transitionClip);

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || _sfxSource == null)
                return;
            
            _sfxSource.PlayOneShot(clip, sfxVolume);
        }
    }
}