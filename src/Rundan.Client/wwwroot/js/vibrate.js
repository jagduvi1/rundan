// Vibration interop. Loaded as a classic script so it's a global the .NET
// VibrationInterop can call. The Vibration API is a no-op where unsupported (iOS Safari).
window.rundanVibrate = function (pattern) {
    try {
        if ('vibrate' in navigator) {
            navigator.vibrate(pattern);
        }
    } catch {
        /* ignore — vibration is a nice-to-have */
    }
};
