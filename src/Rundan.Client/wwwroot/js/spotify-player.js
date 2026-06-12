// Spotify Web Playback SDK wrapper — full-track playback in the host's browser (Premium only).
// The access token is fetched on demand from .NET (tokenRef.GetSpotifyTokenJs), which calls the server
// (the long-lived refresh token stays server-side).

let player = null;
let deviceId = null;
let tokenRef = null;

function loadSdk() {
    return new Promise((resolve) => {
        if (window.Spotify && window.Spotify.Player) { resolve(); return; }
        window.onSpotifyWebPlaybackSDKReady = () => resolve();
        if (!document.getElementById('spotify-sdk')) {
            const s = document.createElement('script');
            s.id = 'spotify-sdk';
            s.src = 'https://sdk.scdn.co/spotify-player.js';
            document.head.appendChild(s);
        }
    });
}

function toTrackUri(s) {
    if (!s) return null;
    if (s.startsWith('spotify:track:')) return s;
    const m = s.match(/track[:/]([A-Za-z0-9]+)/);
    return m ? 'spotify:track:' + m[1] : null;
}

// Returns true once a playback device is ready. dotNetTokenProvider must expose GetSpotifyTokenJs().
export async function init(dotNetTokenProvider) {
    tokenRef = dotNetTokenProvider;
    if (player && deviceId) return true;
    await loadSdk();

    let resolveReady;
    const ready = new Promise(r => (resolveReady = r));

    player = new Spotify.Player({
        name: 'Rundan (host)',
        getOAuthToken: cb => { tokenRef.invokeMethodAsync('GetSpotifyTokenJs').then(t => cb(t || '')); },
        volume: 0.8,
    });
    player.addListener('ready', ({ device_id }) => { deviceId = device_id; resolveReady(true); });
    player.addListener('not_ready', () => { deviceId = null; });
    player.addListener('initialization_error', e => console.error('spotify init', e.message));
    player.addListener('authentication_error', e => console.error('spotify auth', e.message));
    player.addListener('account_error', e => console.error('spotify account (Premium required?)', e.message));

    const connected = await player.connect();
    if (!connected) return false;
    return await Promise.race([ready, new Promise(r => setTimeout(() => r(!!deviceId), 8000))]);
}

// Start a track (accepts an open.spotify.com/track/… URL or a spotify:track: URI).
export async function play(spotifyUrl) {
    const uri = toTrackUri(spotifyUrl);
    if (!player || !deviceId || !tokenRef || !uri) return false;
    const token = await tokenRef.invokeMethodAsync('GetSpotifyTokenJs');
    if (!token) return false;
    const resp = await fetch(`https://api.spotify.com/v1/me/player/play?device_id=${encodeURIComponent(deviceId)}`, {
        method: 'PUT',
        headers: { 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' },
        body: JSON.stringify({ uris: [uri] }),
    });
    return resp.ok;
}

export async function pause() { if (player) { try { await player.pause(); } catch (e) { } } }
export async function resume() { if (player) { try { await player.resume(); } catch (e) { } } }

export function disconnect() {
    if (player) { try { player.disconnect(); } catch (e) { } }
    player = null;
    deviceId = null;
    tokenRef = null;
}
