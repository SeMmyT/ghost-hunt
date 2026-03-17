using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GhostHunt.Network.Transport
{
    /// <summary>
    /// WebTransport bridge for browser clients.
    /// Replaces WebSocket relay with WebTransport datagrams (UDP-like, unreliable).
    /// Falls back to WebSocket if WebTransport unavailable.
    ///
    /// Architecture:
    /// Browser → WebTransport (datagrams) → Edge proxy → UDP → Photon relay
    ///
    /// Expected latency improvement: ~120ms WebSocket → ~60-70ms WebTransport
    /// because datagrams skip TCP head-of-line blocking.
    ///
    /// The edge proxy runs alongside Hathora/Edgegap dedicated server.
    /// It terminates WebTransport and bridges to Photon's UDP transport.
    /// </summary>
    public class WebTransportBridge : MonoBehaviour
    {
        public enum TransportState
        {
            Disconnected,
            Connecting,
            Connected,
            Fallback // Using WebSocket
        }

        public TransportState State { get; private set; } = TransportState.Disconnected;
        public float CurrentRTT { get; private set; }
        public bool IsWebTransport => State == TransportState.Connected;
        public bool IsFallback => State == TransportState.Fallback;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<byte[]> OnDataReceived;

        private string _serverUrl;
        private float _rttSampleTimer;
        private readonly Queue<byte[]> _sendQueue = new();
        private readonly Queue<byte[]> _recvQueue = new();

#if UNITY_WEBGL && !UNITY_EDITOR

        // JavaScript interop for WebTransport API
        [DllImport("__Internal")]
        private static extern int WebTransport_Connect(string url);

        [DllImport("__Internal")]
        private static extern void WebTransport_Send(int connectionId, byte[] data, int length);

        [DllImport("__Internal")]
        private static extern void WebTransport_SendUnreliable(int connectionId, byte[] data, int length);

        [DllImport("__Internal")]
        private static extern int WebTransport_GetState(int connectionId);

        [DllImport("__Internal")]
        private static extern void WebTransport_Close(int connectionId);

        [DllImport("__Internal")]
        private static extern int WebTransport_IsSupported();

        private int _connectionId = -1;

        /// <summary>
        /// Called from JavaScript when data arrives.
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(Action<IntPtr, int>))]
        public static void OnJSDataReceived(IntPtr dataPtr, int length)
        {
            byte[] data = new byte[length];
            Marshal.Copy(dataPtr, data, 0, length);
            _instance?._recvQueue.Enqueue(data);
        }

        private static WebTransportBridge _instance;

        private void Awake()
        {
            _instance = this;
        }

#endif

        /// <summary>
        /// Connect to the WebTransport edge proxy.
        /// URL format: https://edge-proxy.example.com:4433/room/{roomCode}
        /// Falls back to WebSocket if WebTransport not supported.
        /// </summary>
        public void Connect(string serverUrl, string roomCode)
        {
            _serverUrl = $"{serverUrl}/room/{roomCode}";
            State = TransportState.Connecting;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (WebTransport_IsSupported() == 1)
            {
                Debug.Log($"[WebTransport] Connecting via WebTransport: {_serverUrl}");
                _connectionId = WebTransport_Connect(_serverUrl);
            }
            else
            {
                Debug.Log("[WebTransport] Not supported, falling back to WebSocket");
                ConnectWebSocket();
            }
#else
            // Non-WebGL: use native UDP (no bridge needed)
            Debug.Log("[WebTransport] Non-WebGL platform, using native transport");
            State = TransportState.Connected;
            OnConnected?.Invoke();
#endif
        }

        /// <summary>
        /// Send data via unreliable datagram (position updates, input).
        /// Falls back to reliable if WebTransport unavailable.
        /// </summary>
        public void SendUnreliable(byte[] data)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (IsWebTransport && _connectionId >= 0)
            {
                WebTransport_SendUnreliable(_connectionId, data, data.Length);
            }
            else
            {
                // Fallback: reliable send (WebSocket)
                SendReliable(data);
            }
#else
            _sendQueue.Enqueue(data);
#endif
        }

        /// <summary>
        /// Send data via reliable stream (game events, RPCs).
        /// </summary>
        public void SendReliable(byte[] data)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_connectionId >= 0)
            {
                WebTransport_Send(_connectionId, data, data.Length);
            }
#else
            _sendQueue.Enqueue(data);
#endif
        }

        public void Disconnect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_connectionId >= 0)
            {
                WebTransport_Close(_connectionId);
                _connectionId = -1;
            }
#endif
            State = TransportState.Disconnected;
            OnDisconnected?.Invoke();
        }

        private void Update()
        {
            // Process received data
            while (_recvQueue.Count > 0)
            {
                var data = _recvQueue.Dequeue();
                OnDataReceived?.Invoke(data);
            }

            // RTT sampling
            _rttSampleTimer -= Time.deltaTime;
            if (_rttSampleTimer <= 0 && State == TransportState.Connected)
            {
                _rttSampleTimer = 2f; // Sample every 2 seconds
                SendPing();
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // Check connection state
            if (State == TransportState.Connecting && _connectionId >= 0)
            {
                int jsState = WebTransport_GetState(_connectionId);
                if (jsState == 1) // Connected
                {
                    State = TransportState.Connected;
                    Debug.Log("[WebTransport] Connected via datagrams");
                    OnConnected?.Invoke();
                }
                else if (jsState == 3) // Failed
                {
                    Debug.Log("[WebTransport] Connection failed, falling back to WebSocket");
                    ConnectWebSocket();
                }
            }
#endif
        }

        private void ConnectWebSocket()
        {
            // WebSocket fallback URL: same server, /ws/ path
            string wsUrl = _serverUrl.Replace("https://", "wss://") + "/ws";
            Debug.Log($"[WebTransport] Fallback WebSocket: {wsUrl}");

            // TODO: Use Photon's built-in WebSocket transport
            State = TransportState.Fallback;
            OnConnected?.Invoke();
        }

        private void SendPing()
        {
            // Lightweight ping for RTT measurement
            byte[] ping = { 0xFF, 0x00 }; // Ping marker
            SendUnreliable(ping);
        }

        /// <summary>
        /// Called when pong received. Updates RTT.
        /// </summary>
        public void OnPongReceived(float rtt)
        {
            // Exponential moving average
            CurrentRTT = CurrentRTT > 0 ? CurrentRTT * 0.7f + rtt * 0.3f : rtt;
        }
    }
}
