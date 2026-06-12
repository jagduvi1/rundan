// Spotify Authorization Code + PKCE (public client — no secret in the browser).
// startLogin() stashes the PKCE verifier + a CSRF state in sessionStorage and redirects to Spotify;
// readCallback() (on the /spotify-callback page) reads the code back and the matching verifier.

function base64url(bytes) {
    let bin = '';
    for (const b of bytes) bin += String.fromCharCode(b);
    return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export async function startLogin(clientId, redirectUri, scope) {
    const verifier = base64url(crypto.getRandomValues(new Uint8Array(64)));
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
    const challenge = base64url(new Uint8Array(digest));
    const state = base64url(crypto.getRandomValues(new Uint8Array(16)));

    sessionStorage.setItem('spotify_verifier', verifier);
    sessionStorage.setItem('spotify_state', state);

    const url = new URL('https://accounts.spotify.com/authorize');
    url.searchParams.set('client_id', clientId);
    url.searchParams.set('response_type', 'code');
    url.searchParams.set('redirect_uri', redirectUri);
    url.searchParams.set('code_challenge_method', 'S256');
    url.searchParams.set('code_challenge', challenge);
    url.searchParams.set('scope', scope);
    url.searchParams.set('state', state);
    window.location.assign(url.toString());
}

export function readCallback() {
    const params = new URLSearchParams(window.location.search);
    const state = params.get('state');
    const savedState = sessionStorage.getItem('spotify_state');
    const verifier = sessionStorage.getItem('spotify_verifier');
    sessionStorage.removeItem('spotify_verifier');
    sessionStorage.removeItem('spotify_state');
    return {
        code: params.get('code'),
        error: params.get('error'),
        verifier: verifier || '',
        stateOk: !!state && state === savedState,
    };
}

export function redirectUri() {
    return window.location.origin + '/spotify-callback';
}
