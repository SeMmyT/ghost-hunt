using System.Collections.Generic;
using GhostHunt.Core;
using GhostHunt.Network;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunt.UI
{
    /// <summary>
    /// Minimap radar showing nearby players.
    /// Ghost view: limited range, shows teammates + target ping (role-dependent).
    /// Target view: full maze overview with ghost trails.
    /// </summary>
    public class RadarUI : MonoBehaviour
    {
        [SerializeField] private RectTransform _radarContainer;
        [SerializeField] private GameObject _blipPrefab; // Small dot for each entity
        [SerializeField] private float _ghostRadarRange = 15f;
        [SerializeField] private float _targetRadarRange = 100f; // Effectively full map
        [SerializeField] private float _radarScale = 3f; // World units to UI pixels

        private PlayerController _localPlayer;
        private readonly List<GameObject> _blipPool = new();
        private int _activeBlips;

        private void Update()
        {
            if (_localPlayer == null)
            {
                foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                {
                    if (pc.HasInputAuthority)
                    {
                        _localPlayer = pc;
                        break;
                    }
                }
                return;
            }

            _activeBlips = 0;

            float range = _localPlayer.State.Role == PlayerRole.Target
                ? _targetRadarRange
                : _ghostRadarRange;

            // Show all players within range
            foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (player == _localPlayer) continue;

                float dist = Vector3.Distance(
                    new Vector3(_localPlayer.State.Position.X, 0, _localPlayer.State.Position.Z),
                    new Vector3(player.State.Position.X, 0, player.State.Position.Z)
                );

                if (dist > range) continue;

                // Role-specific visibility
                if (!ShouldShowOnRadar(_localPlayer.State.Role, player.State.Role))
                    continue;

                PlaceBlip(player, dist);
            }

            // Hide unused blips
            for (int i = _activeBlips; i < _blipPool.Count; i++)
                _blipPool[i].SetActive(false);
        }

        private void PlaceBlip(PlayerController other, float distance)
        {
            var blip = GetOrCreateBlip();

            // Calculate relative position on radar
            Vector3 localPos = new(_localPlayer.State.Position.X, 0, _localPlayer.State.Position.Z);
            Vector3 otherPos = new(other.State.Position.X, 0, other.State.Position.Z);
            Vector3 offset = otherPos - localPos;

            // Rotate to match player facing (for ghost first-person view)
            float playerYaw = Mathf.Atan2(
                _localPlayer.State.Rotation.Y,
                _localPlayer.State.Rotation.W
            ) * 2f * Mathf.Rad2Deg;

            Vector3 rotated = Quaternion.Euler(0, -playerYaw, 0) * offset;

            blip.GetComponent<RectTransform>().anchoredPosition = new Vector2(
                rotated.x * _radarScale,
                rotated.z * _radarScale
            );

            // Color by role
            var image = blip.GetComponent<Image>();
            if (image != null)
            {
                image.color = other.State.Role switch
                {
                    PlayerRole.Target => Color.white, // Target is bright on radar
                    PlayerRole.Chaser => new Color(0.8f, 0.2f, 0.2f),
                    PlayerRole.Ambusher => new Color(0.8f, 0.4f, 0.6f),
                    PlayerRole.Flanker => new Color(0.2f, 0.6f, 0.8f),
                    PlayerRole.Wildcard => new Color(0.8f, 0.6f, 0.2f),
                    _ => Color.gray
                };
            }

            blip.SetActive(true);
        }

        private bool ShouldShowOnRadar(PlayerRole viewerRole, PlayerRole otherRole)
        {
            // Target sees all ghosts (full awareness)
            if (viewerRole == PlayerRole.Target)
                return true;

            // Ghosts always see teammates
            if (otherRole != PlayerRole.Target)
                return true;

            // Ghost seeing target depends on role:
            // Chaser: always sees target
            // Ambusher: sees predictive trail (handled separately)
            // Flanker: sees intercept point (handled separately)
            // Wildcard: no direct target visibility
            return viewerRole == PlayerRole.Chaser;
        }

        private GameObject GetOrCreateBlip()
        {
            if (_activeBlips < _blipPool.Count)
            {
                return _blipPool[_activeBlips++];
            }

            var blip = Instantiate(_blipPrefab, _radarContainer);
            _blipPool.Add(blip);
            _activeBlips++;
            return blip;
        }
    }
}
