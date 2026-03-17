// WebTransport JavaScript interop for Unity WebGL builds.
// Provides UDP-like unreliable datagrams in browsers.
// Falls back gracefully if WebTransport API unavailable.

var WebTransportPlugin = {

    $connections: {},
    $nextId: 0,
    $dataCallback: null,

    WebTransport_IsSupported: function() {
        return (typeof WebTransport !== 'undefined') ? 1 : 0;
    },

    WebTransport_Connect: function(urlPtr) {
        var url = UTF8ToString(urlPtr);
        var id = nextId++;

        try {
            var wt = new WebTransport(url);
            connections[id] = {
                transport: wt,
                state: 0, // 0=connecting, 1=connected, 2=closing, 3=failed
                writer: null,
                datagramWriter: null
            };

            wt.ready.then(function() {
                connections[id].state = 1;

                // Set up reliable stream
                wt.createBidirectionalStream().then(function(stream) {
                    connections[id].writer = stream.writable.getWriter();

                    // Read from stream
                    var reader = stream.readable.getReader();
                    function readLoop() {
                        reader.read().then(function(result) {
                            if (result.done) return;
                            var data = result.value;
                            var buf = _malloc(data.byteLength);
                            HEAPU8.set(new Uint8Array(data.buffer, data.byteOffset, data.byteLength), buf);
                            dynCall_vii(dataCallback, buf, data.byteLength);
                            _free(buf);
                            readLoop();
                        });
                    }
                    readLoop();
                });

                // Set up datagram channel (unreliable, UDP-like)
                if (wt.datagrams) {
                    connections[id].datagramWriter = wt.datagrams.writable.getWriter();

                    // Read datagrams
                    var dgReader = wt.datagrams.readable.getReader();
                    function readDatagrams() {
                        dgReader.read().then(function(result) {
                            if (result.done) return;
                            var data = result.value;
                            var buf = _malloc(data.byteLength);
                            HEAPU8.set(new Uint8Array(data.buffer, data.byteOffset, data.byteLength), buf);
                            dynCall_vii(dataCallback, buf, data.byteLength);
                            _free(buf);
                            readDatagrams();
                        });
                    }
                    readDatagrams();
                }

                console.log('[WebTransport] Connected: ' + url);
            }).catch(function(err) {
                connections[id].state = 3;
                console.error('[WebTransport] Connection failed:', err);
            });

            wt.closed.then(function() {
                connections[id].state = 2;
                console.log('[WebTransport] Closed');
            });

        } catch (err) {
            connections[id] = { state: 3 };
            console.error('[WebTransport] Error:', err);
        }

        return id;
    },

    WebTransport_Send: function(connectionId, dataPtr, length) {
        var conn = connections[connectionId];
        if (!conn || !conn.writer) return;

        var data = new Uint8Array(HEAPU8.buffer, dataPtr, length);
        conn.writer.write(new Uint8Array(data));
    },

    WebTransport_SendUnreliable: function(connectionId, dataPtr, length) {
        var conn = connections[connectionId];
        if (!conn) return;

        var data = new Uint8Array(HEAPU8.buffer, dataPtr, length);

        // Use datagrams if available (unreliable, low latency)
        if (conn.datagramWriter) {
            conn.datagramWriter.write(new Uint8Array(data));
        } else if (conn.writer) {
            // Fallback to reliable stream
            conn.writer.write(new Uint8Array(data));
        }
    },

    WebTransport_GetState: function(connectionId) {
        var conn = connections[connectionId];
        return conn ? conn.state : 3;
    },

    WebTransport_Close: function(connectionId) {
        var conn = connections[connectionId];
        if (conn && conn.transport) {
            conn.transport.close();
        }
        delete connections[connectionId];
    }
};

autoAddDeps(WebTransportPlugin, '$connections');
autoAddDeps(WebTransportPlugin, '$nextId');
autoAddDeps(WebTransportPlugin, '$dataCallback');
mergeInto(LibraryManager.library, WebTransportPlugin);
