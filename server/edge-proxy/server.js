/**
 * Ghost Hunt — WebTransport Edge Proxy
 *
 * Bridges browser WebTransport datagrams to Photon's UDP relay.
 * Runs alongside the game's dedicated server (Hathora/Edgegap).
 *
 * Architecture:
 *   Browser ─── WebTransport (QUIC datagrams) ──→ This proxy ──→ UDP ──→ Photon relay
 *
 * Why:
 *   Browsers can't use UDP directly. WebSocket adds ~70ms overhead (TCP HOL blocking).
 *   WebTransport datagrams are unreliable like UDP — no HOL blocking, ~60ms RTT.
 *
 * Usage:
 *   PHOTON_RELAY=ns.exitgames.com:5058 node server.js
 *
 * Requires: Node 20+ with experimental WebTransport support, TLS certificate.
 */

import { createServer } from 'node:http2';
import { readFileSync } from 'node:fs';
import { createSocket } from 'node:dgram';

const PHOTON_RELAY = process.env.PHOTON_RELAY || 'ns.exitgames.com:5058';
const PORT = parseInt(process.env.PORT || '4433');
const CERT_PATH = process.env.CERT_PATH || './certs/cert.pem';
const KEY_PATH = process.env.KEY_PATH || './certs/key.pem';

// Room → { clients: Map<sessionId, { udpSocket, photonAddr }> }
const rooms = new Map();

// Ping/pong for RTT measurement
const PING_MARKER = 0xFF;
const PONG_MARKER = 0xFE;

/**
 * Start HTTP/2 server with WebTransport support.
 * Each browser client opens a WebTransport session to /room/{roomCode}.
 */
function startServer() {
  let serverOptions;

  try {
    serverOptions = {
      key: readFileSync(KEY_PATH),
      cert: readFileSync(CERT_PATH),
      allowHTTP1: false
    };
  } catch {
    console.error(`[EdgeProxy] Certificate not found at ${CERT_PATH} / ${KEY_PATH}`);
    console.error('[EdgeProxy] Generate with: openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -keyout certs/key.pem -out certs/cert.pem -days 365 -nodes -subj "/CN=localhost"');
    process.exit(1);
  }

  const server = createServer(serverOptions);

  server.on('stream', (stream, headers) => {
    const path = headers[':path'];
    const method = headers[':method'];

    // Health check
    if (path === '/health') {
      stream.respond({ ':status': 200, 'content-type': 'text/plain' });
      stream.end('ok');
      return;
    }

    // Status endpoint
    if (path === '/status') {
      const status = {};
      for (const [roomCode, room] of rooms) {
        status[roomCode] = { clients: room.clients.size };
      }
      stream.respond({ ':status': 200, 'content-type': 'application/json' });
      stream.end(JSON.stringify(status, null, 2));
      return;
    }

    // WebTransport upgrade for /room/{code}
    const match = path?.match(/^\/room\/([A-Z0-9]{6})$/);
    if (!match) {
      stream.respond({ ':status': 404 });
      stream.end('Not found. Use /room/{ROOMCODE}');
      return;
    }

    const roomCode = match[1];
    console.log(`[EdgeProxy] WebTransport session for room ${roomCode}`);

    // Check if this is a WebTransport CONNECT request
    if (method === 'CONNECT' && headers[':protocol'] === 'webtransport') {
      handleWebTransportSession(stream, roomCode);
    } else {
      stream.respond({ ':status': 400 });
      stream.end('Expected WebTransport CONNECT');
    }
  });

  server.listen(PORT, () => {
    console.log(`[EdgeProxy] WebTransport proxy listening on :${PORT}`);
    console.log(`[EdgeProxy] Photon relay: ${PHOTON_RELAY}`);
  });
}

/**
 * Handle a single WebTransport session.
 * Creates a UDP socket to bridge to Photon relay.
 */
function handleWebTransportSession(stream, roomCode) {
  const sessionId = crypto.randomUUID();
  const [photonHost, photonPort] = PHOTON_RELAY.split(':');

  // Create UDP socket for this client → Photon relay
  const udpSocket = createSocket('udp4');

  // Get or create room
  if (!rooms.has(roomCode)) {
    rooms.set(roomCode, { clients: new Map() });
  }
  const room = rooms.get(roomCode);
  room.clients.set(sessionId, { udpSocket, stream });

  console.log(`[EdgeProxy] Client ${sessionId.slice(0, 8)} joined room ${roomCode} (${room.clients.size} clients)`);

  // Accept the WebTransport session
  stream.respond({ ':status': 200 });

  // Browser → Proxy → Photon
  stream.on('data', (data) => {
    // Check for ping
    if (data.length === 2 && data[0] === PING_MARKER) {
      // Respond with pong immediately
      const pong = Buffer.from([PONG_MARKER, data[1]]);
      stream.write(pong);
      return;
    }

    // Forward to Photon relay via UDP
    udpSocket.send(data, 0, data.length, parseInt(photonPort), photonHost);
  });

  // Photon → Proxy → Browser
  udpSocket.on('message', (msg) => {
    if (!stream.destroyed) {
      stream.write(msg);
    }
  });

  // Cleanup on disconnect
  stream.on('close', () => {
    console.log(`[EdgeProxy] Client ${sessionId.slice(0, 8)} disconnected from room ${roomCode}`);
    udpSocket.close();
    room.clients.delete(sessionId);
    if (room.clients.size === 0) {
      rooms.delete(roomCode);
    }
  });

  stream.on('error', (err) => {
    console.error(`[EdgeProxy] Stream error for ${sessionId.slice(0, 8)}:`, err.message);
    udpSocket.close();
    room.clients.delete(sessionId);
  });

  udpSocket.on('error', (err) => {
    console.error(`[EdgeProxy] UDP error for ${sessionId.slice(0, 8)}:`, err.message);
  });
}

startServer();
