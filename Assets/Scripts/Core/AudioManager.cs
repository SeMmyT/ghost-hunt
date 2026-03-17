using GhostHunt.Core;
using UnityEngine;

namespace GhostHunt.Audio
{
    /// <summary>
    /// Manages game audio. Minimal but atmospheric.
    /// Ghost wails, power pellet alarms, catch stingers, proximity tension.
    /// Spatial audio for VR, stereo for flat platforms.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Header("Stingers")]
        [SerializeField] private AudioClip _catchStinger;
        [SerializeField] private AudioClip _powerPelletAlarm;
        [SerializeField] private AudioClip _roundStartChime;
        [SerializeField] private AudioClip _countdownTick;

        [Header("Ambient")]
        [SerializeField] private AudioClip _huntAmbience;
        [SerializeField] private AudioClip _frightenedAmbience;

        [Header("Ghost")]
        [SerializeField] private AudioClip _ghostWail;
        [SerializeField] private AudioClip _ghostBurst;

        [Header("Target")]
        [SerializeField] private AudioClip _collectiblePickup;
        [SerializeField] private AudioClip _speedBoostActivate;
        [SerializeField] private AudioClip _decoyDrop;

        private AudioSource _musicSource;
        private AudioSource _sfxSource;

        public static AudioManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.volume = 0.3f;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.volume = 0.8f;
        }

        public void PlayPhaseMusic(GamePhase phase)
        {
            AudioClip clip = phase switch
            {
                GamePhase.Hunt => _huntAmbience,
                GamePhase.Frightened => _frightenedAmbience,
                _ => null
            };

            if (clip != null && _musicSource.clip != clip)
            {
                _musicSource.clip = clip;
                _musicSource.Play();
            }
            else if (clip == null)
            {
                _musicSource.Stop();
            }
        }

        public void PlayCatch() => PlaySFX(_catchStinger);
        public void PlayPowerPellet() => PlaySFX(_powerPelletAlarm);
        public void PlayRoundStart() => PlaySFX(_roundStartChime);
        public void PlayCountdownTick() => PlaySFX(_countdownTick);
        public void PlayGhostWail() => PlaySFX(_ghostWail);
        public void PlayGhostBurst() => PlaySFX(_ghostBurst);
        public void PlayCollectible() => PlaySFX(_collectiblePickup);
        public void PlaySpeedBoost() => PlaySFX(_speedBoostActivate);
        public void PlayDecoy() => PlaySFX(_decoyDrop);

        /// <summary>
        /// Play spatial audio at a world position. Used for VR proximity effects.
        /// </summary>
        public void PlaySpatial(AudioClip clip, Vector3 position, float maxDistance = 15f)
        {
            if (clip == null) return;

            var tempGo = new GameObject("SpatialAudio");
            tempGo.transform.position = position;

            var source = tempGo.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 1f; // Full 3D
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.Play();

            Destroy(tempGo, clip.length + 0.1f);
        }

        private void PlaySFX(AudioClip clip)
        {
            if (clip != null)
                _sfxSource.PlayOneShot(clip);
        }
    }
}
