// Browser utility functions for Unity WebGL builds.
// Handles URL parameters, clipboard, fullscreen, device detection.

var BrowserUtilsPlugin = {

    GetURLParameter: function(namePtr) {
        var name = UTF8ToString(namePtr);
        var params = new URLSearchParams(window.location.search);
        var value = params.get(name) || '';
        var bufferSize = lengthBytesUTF8(value) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(value, buffer, bufferSize);
        return buffer;
    },

    IsMobileDevice: function() {
        var isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
        return isMobile ? 1 : 0;
    },

    RequestFullscreen: function() {
        var canvas = document.querySelector('#unity-canvas') || document.querySelector('canvas');
        if (canvas) {
            if (canvas.requestFullscreen) {
                canvas.requestFullscreen();
            } else if (canvas.webkitRequestFullscreen) {
                canvas.webkitRequestFullscreen();
            } else if (canvas.mozRequestFullScreen) {
                canvas.mozRequestFullScreen();
            }
        }
    },

    CopyToClipboard: function(textPtr) {
        var text = UTF8ToString(textPtr);
        if (navigator.clipboard) {
            navigator.clipboard.writeText(text).then(function() {
                console.log('[GhostHunt] Link copied to clipboard');
            });
        } else {
            // Fallback for older browsers
            var textarea = document.createElement('textarea');
            textarea.value = text;
            document.body.appendChild(textarea);
            textarea.select();
            document.execCommand('copy');
            document.body.removeChild(textarea);
        }
    },

    SetPageTitle: function(titlePtr) {
        document.title = UTF8ToString(titlePtr);
    }
};

mergeInto(LibraryManager.library, BrowserUtilsPlugin);
