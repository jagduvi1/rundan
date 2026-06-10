// Minimal service worker — Web Push only (no offline caching, so it won't interfere with Blazor).
self.addEventListener('push', event => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch (e) { data = { body: event.data && event.data.text() }; }
    const title = data.title || 'Rundan';
    const options = {
        body: data.body || '',
        icon: 'img/rundan-mark.svg',
        badge: 'img/rundan-mark.svg',
        tag: data.tag,           // same tag collapses repeats (e.g. one "new chat" bubble)
        renotify: !!data.tag,
        data: { url: data.url || '/' },
    };
    event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = (event.notification.data && event.notification.data.url) || '/';
    event.waitUntil((async () => {
        const all = await clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const c of all) {
            if ('focus' in c) { try { await c.navigate(url); } catch (e) { } return c.focus(); }
        }
        if (clients.openWindow) return clients.openWindow(url);
    })());
});
