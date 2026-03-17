using System.Collections.Generic;
using Fusion;
using GhostHunt.Core;
using UnityEngine;

namespace GhostHunt.Network.Prediction
{
    /// <summary>
    /// Client-side prediction with server reconciliation.
    /// Makes high-latency browser clients feel responsive.
    ///
    /// How it works:
    /// 1. Client applies input locally immediately (no wait for server)
    /// 2. Client stores input + predicted state in a buffer
    /// 3. When server state arrives, client compares against predicted state
    /// 4. If mismatch, client replays buffered inputs from the server-confirmed state
    ///
    /// For browser at 120ms RTT: player sees their own movement instantly,
    /// other players are interpolated with ~60ms visual delay.
    /// At 60ms (WebTransport): nearly indistinguishable from native.
    /// </summary>
    public class ClientPrediction : NetworkBehaviour
    {
        [SerializeField] private float _correctionSmoothing = 10f;
        [SerializeField] private float _correctionThreshold = 0.1f; // Below this, don't correct
        [SerializeField] private float _snapThreshold = 3f; // Above this, snap instead of lerp

        private readonly Queue<PredictedFrame> _predictionBuffer = new();
        private const int MaxBufferSize = 128; // ~2 seconds at 60Hz

        private Vector3 _predictedPosition;
        private Vector3 _serverPosition;
        private Vector3 _displayPosition; // What the player actually sees (smoothed)
        private bool _needsReconciliation;

        public struct PredictedFrame
        {
            public int Tick;
            public Vector2 Input;
            public Vector3 PredictedPosition;
            public float Speed;
        }

        /// <summary>
        /// Apply input locally and store prediction.
        /// Called BEFORE sending input to server.
        /// </summary>
        public Vector3 PredictMove(Vector2 moveInput, float speed, int currentTick)
        {
            // Apply movement locally
            Vector3 movement = new Vector3(moveInput.x, 0, moveInput.y) * speed * Runner.DeltaTime;
            _predictedPosition = transform.position + movement;

            // Store in buffer for reconciliation
            _predictionBuffer.Enqueue(new PredictedFrame
            {
                Tick = currentTick,
                Input = moveInput,
                PredictedPosition = _predictedPosition,
                Speed = speed
            });

            // Trim buffer
            while (_predictionBuffer.Count > MaxBufferSize)
                _predictionBuffer.Dequeue();

            return _predictedPosition;
        }

        /// <summary>
        /// Called when authoritative server state arrives.
        /// Compares against our prediction and corrects if needed.
        /// </summary>
        public void OnServerStateReceived(Vector3 serverPos, int serverTick)
        {
            _serverPosition = serverPos;

            // Find our prediction for this tick
            PredictedFrame? matchingFrame = null;
            var remaining = new Queue<PredictedFrame>();

            while (_predictionBuffer.Count > 0)
            {
                var frame = _predictionBuffer.Dequeue();
                if (frame.Tick == serverTick)
                {
                    matchingFrame = frame;
                    // Keep frames AFTER this tick (they haven't been confirmed yet)
                    break;
                }
                // Discard frames older than server tick
            }

            // Move remaining frames back to buffer
            while (_predictionBuffer.Count > 0)
                remaining.Enqueue(_predictionBuffer.Dequeue());
            while (remaining.Count > 0)
                _predictionBuffer.Enqueue(remaining.Dequeue());

            if (!matchingFrame.HasValue) return;

            // Compare prediction vs server
            float error = Vector3.Distance(matchingFrame.Value.PredictedPosition, serverPos);

            if (error < _correctionThreshold)
            {
                // Prediction was accurate — no correction needed
                return;
            }

            if (error > _snapThreshold)
            {
                // Way off — snap to server position (probably teleported)
                _predictedPosition = serverPos;
                _displayPosition = serverPos;
                transform.position = serverPos;
                Debug.Log($"[Prediction] Snapped to server (error: {error:F2}m)");
                return;
            }

            // Moderate error — replay inputs from server-confirmed position
            _needsReconciliation = true;
            Reconcile(serverPos);
        }

        /// <summary>
        /// Replay buffered inputs from server-confirmed position.
        /// </summary>
        private void Reconcile(Vector3 confirmedPosition)
        {
            Vector3 replayPos = confirmedPosition;

            // Replay all unconfirmed inputs
            foreach (var frame in _predictionBuffer)
            {
                Vector3 movement = new Vector3(frame.Input.x, 0, frame.Input.y)
                    * frame.Speed * Runner.DeltaTime;
                replayPos += movement;
            }

            _predictedPosition = replayPos;
        }

        private void Update()
        {
            if (!HasInputAuthority) return;

            // Smooth visual position toward predicted position
            if (_needsReconciliation)
            {
                _displayPosition = Vector3.Lerp(
                    _displayPosition,
                    _predictedPosition,
                    Time.deltaTime * _correctionSmoothing
                );

                if (Vector3.Distance(_displayPosition, _predictedPosition) < 0.01f)
                {
                    _displayPosition = _predictedPosition;
                    _needsReconciliation = false;
                }
            }
            else
            {
                _displayPosition = _predictedPosition;
            }

            // Apply smoothed position to visual transform
            transform.position = _displayPosition;
        }
    }

    /// <summary>
    /// Interpolates remote player positions for smooth display.
    /// Browser clients see other players with ~60ms visual delay
    /// but smooth movement (no jitter).
    /// </summary>
    public class RemotePlayerInterpolation : MonoBehaviour
    {
        [SerializeField] private float _interpolationDelay = 0.1f; // 100ms buffer
        [SerializeField] private float _maxExtrapolation = 0.2f;

        private readonly List<PositionSnapshot> _snapshots = new();
        private const int MaxSnapshots = 30;

        public struct PositionSnapshot
        {
            public float Timestamp;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        /// <summary>
        /// Add a new network state snapshot.
        /// Called when we receive a remote player's position from the server.
        /// </summary>
        public void AddSnapshot(Vector3 position, Quaternion rotation)
        {
            _snapshots.Add(new PositionSnapshot
            {
                Timestamp = Time.time,
                Position = position,
                Rotation = rotation
            });

            while (_snapshots.Count > MaxSnapshots)
                _snapshots.RemoveAt(0);
        }

        private void Update()
        {
            if (_snapshots.Count < 2) return;

            // Render at (now - interpolationDelay) for smooth display
            float renderTime = Time.time - _interpolationDelay;

            // Find two snapshots that bracket the render time
            for (int i = 0; i < _snapshots.Count - 1; i++)
            {
                var a = _snapshots[i];
                var b = _snapshots[i + 1];

                if (renderTime >= a.Timestamp && renderTime <= b.Timestamp)
                {
                    float t = (renderTime - a.Timestamp) / (b.Timestamp - a.Timestamp);
                    transform.position = Vector3.Lerp(a.Position, b.Position, t);
                    transform.rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
                    return;
                }
            }

            // If render time is beyond latest snapshot, extrapolate briefly
            if (_snapshots.Count >= 2)
            {
                var last = _snapshots[^1];
                var prev = _snapshots[^2];
                float timeSince = Time.time - last.Timestamp;

                if (timeSince < _maxExtrapolation)
                {
                    float dt = last.Timestamp - prev.Timestamp;
                    if (dt > 0)
                    {
                        Vector3 velocity = (last.Position - prev.Position) / dt;
                        transform.position = last.Position + velocity * timeSince;
                    }
                }
            }
        }
    }
}
