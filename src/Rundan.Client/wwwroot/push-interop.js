// Web Push helpers called from Blazor. Registers the push-only service worker, asks permission,
// and subscribes with the server's VAPID public key.
window.rundanPush = {
    supported() {
        return ('serviceWorker' in navigator) && ('PushManager' in window) && ('Notification' in window);
    },

    async status() {
        if (!this.supported()) return 'unsupported';
        if (Notification.permission === 'denied') return 'denied';
        try {
            const reg = await navigator.serviceWorker.getRegistration();
            if (reg) {
                const sub = await reg.pushManager.getSubscription();
                if (sub) return 'subscribed';
            }
        } catch (e) { }
        return Notification.permission; // 'granted' | 'default'
    },

    async subscribe(vapidPublicKey) {
        if (!this.supported()) return { ok: false, reason: 'unsupported' };
        const perm = await Notification.requestPermission();
        if (perm !== 'granted') return { ok: false, reason: 'denied' };

        const reg = await navigator.serviceWorker.register('service-worker.js');
        await navigator.serviceWorker.ready;

        let sub = await reg.pushManager.getSubscription();
        if (!sub) {
            sub = await reg.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: urlBase64ToUint8Array(vapidPublicKey),
            });
        }
        const json = sub.toJSON();
        return { ok: true, endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
    },

    async unsubscribe() {
        try {
            const reg = await navigator.serviceWorker.getRegistration();
            if (reg) {
                const sub = await reg.pushManager.getSubscription();
                if (sub) await sub.unsubscribe();
            }
        } catch (e) { }
    },
};

function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = atob(base64);
    const arr = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) arr[i] = raw.charCodeAt(i);
    return arr;
}
