// Leaflet interop module. Leaflet itself (global `L`) and its CSS come from the CDN
// referenced in index.html. Tiles are free OpenStreetMap tiles — no API key.

export function createMap(elementId, lat, lng, zoom) {
    const map = L.map(elementId, { zoomControl: true });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(map);

    // The element is often still being sized by Blazor; measure it now (so setView/fitBounds
    // compute the correct zoom) and again after the next layout flush.
    map.invalidateSize();
    map.setView([lat, lng], zoom);
    requestAnimationFrame(() => map.invalidateSize());

    return {
        map,
        questionLayer: L.layerGroup().addTo(map),
        userMarker: null,
        accuracyCircle: null
    };
}

export function setQuestionMarkers(handle, markers) {
    handle.questionLayer.clearLayers();
    for (const m of markers) {
        const color = m.done ? '#22c55e' : '#2563eb';

        L.marker([m.lat, m.lng], { title: m.label })
            .addTo(handle.questionLayer)
            .bindPopup(`<b>${m.label}</b>${m.done ? ' ✓' : ''}`);

        if (m.radius && m.radius > 0) {
            L.circle([m.lat, m.lng], {
                radius: m.radius,
                color,
                weight: 1,
                fillColor: color,
                fillOpacity: 0.08
            }).addTo(handle.questionLayer);
        }
    }
}

export function setUser(handle, lat, lng, accuracy) {
    const latlng = [lat, lng];
    if (!handle.userMarker) {
        handle.userMarker = L.circleMarker(latlng, {
            radius: 8, color: '#dc2626', fillColor: '#dc2626', fillOpacity: 0.9, weight: 2
        }).addTo(handle.map);
    } else {
        handle.userMarker.setLatLng(latlng);
    }

    if (accuracy && accuracy > 0) {
        if (!handle.accuracyCircle) {
            handle.accuracyCircle = L.circle(latlng, {
                radius: accuracy, color: '#dc2626', weight: 1, fillOpacity: 0.05
            }).addTo(handle.map);
        } else {
            handle.accuracyCircle.setLatLng(latlng).setRadius(accuracy);
        }
    }
}

export function panTo(handle, lat, lng) {
    handle.map.panTo([lat, lng]);
}

export function fitToMarkers(handle, markers) {
    if (!markers || markers.length === 0) return;
    handle.map.invalidateSize(); // ensure the zoom is computed against the real viewport size
    const bounds = L.latLngBounds(markers.map(m => [m.lat, m.lng]));
    handle.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 17 });
}

export function dispose(handle) {
    if (handle && handle.map) {
        handle.map.remove();
    }
}

// ---- Location picker: tap (or drag) to set a GPS point + radius ----
export function createPicker(elementId, lat, lng, radius, dotNetRef) {
    const hasPoint = lat !== null && lng !== null && lat !== undefined && lng !== undefined;
    const center = hasPoint ? [lat, lng] : [59.3293, 18.0686]; // Stockholm default
    const map = L.map(elementId, { zoomControl: true }).setView(center, hasPoint ? 16 : 11);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(map);

    const handle = { map, marker: null, circle: null, dotNetRef, radius: radius || 25 };
    function ring(latlng) {
        if (!handle.circle) {
            handle.circle = L.circle(latlng, { radius: handle.radius, color: '#0B84B3', weight: 1, fillColor: '#0B84B3', fillOpacity: 0.1 }).addTo(map);
        } else {
            handle.circle.setLatLng(latlng);
        }
    }
    handle.place = function (latlng) {
        if (!handle.marker) {
            handle.marker = L.marker(latlng, { draggable: true }).addTo(map);
            handle.marker.on('dragend', () => {
                const p = handle.marker.getLatLng();
                ring(p);
                handle.dotNetRef.invokeMethodAsync('OnPicked', p.lat, p.lng);
            });
        } else {
            handle.marker.setLatLng(latlng);
        }
        ring(latlng);
    };

    if (hasPoint) handle.place(center);
    map.on('click', e => { handle.place(e.latlng); handle.dotNetRef.invokeMethodAsync('OnPicked', e.latlng.lat, e.latlng.lng); });
    setTimeout(() => map.invalidateSize(), 200);
    return handle;
}

export function pickerSetRadius(handle, r) { handle.radius = r; if (handle.circle) handle.circle.setRadius(r); }
export function pickerSetCenter(handle, lat, lng) { handle.place([lat, lng]); handle.map.setView([lat, lng], 16); }
export function pickerClear(handle) {
    if (handle.marker) { handle.map.removeLayer(handle.marker); handle.marker = null; }
    if (handle.circle) { handle.map.removeLayer(handle.circle); handle.circle = null; }
}
export function disposePicker(handle) { if (handle && handle.map) handle.map.remove(); }
