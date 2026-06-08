// Geolocation interop. The browser Geolocation API needs HTTPS, which the app
// enforces. Positions are pushed back to .NET via the supplied DotNetObjectReference.

export function startWatch(dotNetRef) {
    if (!('geolocation' in navigator)) {
        dotNetRef.invokeMethodAsync('ReportError', 'This device has no geolocation.');
        return -1;
    }

    return navigator.geolocation.watchPosition(
        pos => dotNetRef.invokeMethodAsync('ReportPosition',
            pos.coords.latitude, pos.coords.longitude, pos.coords.accuracy),
        err => dotNetRef.invokeMethodAsync('ReportError', err.message || 'Could not get your location.'),
        { enableHighAccuracy: true, maximumAge: 4000, timeout: 20000 });
}

// One-shot read, used by the host to drop a question at "my location".
export function getCurrentPosition() {
    return new Promise((resolve, reject) => {
        if (!('geolocation' in navigator)) {
            reject('This device has no geolocation.');
            return;
        }
        navigator.geolocation.getCurrentPosition(
            pos => resolve([pos.coords.latitude, pos.coords.longitude, pos.coords.accuracy]),
            err => reject(err.message || 'Could not get your location.'),
            { enableHighAccuracy: true, timeout: 15000 });
    });
}

export function stopWatch(watchId) {
    if (watchId !== null && watchId >= 0 && 'geolocation' in navigator) {
        navigator.geolocation.clearWatch(watchId);
    }
}
