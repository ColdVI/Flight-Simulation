const viewer = new Cesium.Viewer('cesiumContainer', {
    baseLayerPicker: false,
    geocoder: false,
    timeline: false,
    animation: false,
    selectionIndicator: false,
    infoBox: false,
    sceneModePicker: false,
    terrainProvider: undefined
});

viewer.scene.globe.baseColor = Cesium.Color.fromCssColorString('#101b28');
viewer.scene.skyAtmosphere.show = true;
viewer.scene.skyBox.show = true;
viewer.scene.globe.enableLighting = false;
viewer.scene.highDynamicRange = false;
viewer.scene.backgroundColor = Cesium.Color.fromCssColorString('#05080c');

try {
    const osmLayer = viewer.imageryLayers.addImageryProvider(
        new Cesium.UrlTemplateImageryProvider({
            url: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
            subdomains: ['a', 'b', 'c'],
            tilingScheme: new Cesium.WebMercatorTilingScheme(),
            minimumLevel: 0,
            maximumLevel: 18,
            credit: '© OpenStreetMap contributors'
        })
    );
    osmLayer.alpha = 1.0;
    osmLayer.brightness = 1.15;
    osmLayer.gamma = 0.8;
} catch (err) {
    console.warn('OpenStreetMap imagery could not be added. Showing baseColor only.', err);
}

viewer.camera.setView({
    destination: Cesium.Cartesian3.fromDegrees(35.0, 39.0, 1500000)
});

const connectionStatusEl = document.getElementById('connection-status');
const activeCountEl = document.getElementById('active-count');
const speedValueEl = document.getElementById('speed-val');
const flightListEl = document.getElementById('flight-list');
const searchInputEl = document.getElementById('search-input');

document.getElementById('btn-start').onclick = () => sendControl('start');
document.getElementById('btn-stop').onclick = () => sendControl('stop');
document.getElementById('btn-reset').onclick = () => sendControl('reset');

document.getElementById('speed-slider').oninput = (event) => {
    const val = Number(event.target.value);
    speedValueEl.innerText = `${val.toFixed(1)}x`;
    fetch('http://localhost:5001/api/simulation/speed', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ multiplier: val })
    });
};

searchInputEl.addEventListener('input', () => renderFlightList(latestFlights));

async function sendControl(action) {
    await fetch(`http://localhost:5001/api/simulation/${action}`, { method: 'POST' });
}

const flightEntities = {};
const flightRoutes = {};
let latestFlights = [];
let selectedFlightId = null;

async function fetchFromBackend() {
    try {
        const response = await fetch('http://localhost:5001/api/flights');
        if (!response.ok) throw new Error('API Error');

        const flights = await response.json();
        latestFlights = flights;

        setConnectionState(true);
        activeCountEl.innerText = flights.filter(f => f.status === 'ACTIVE').length;

        updateMap(flights);
        renderFlightList(flights);
        updateRouteVisibility();

        if (selectedFlightId) {
            const selected = flights.find(f => f.callsign === selectedFlightId);
            if (selected) {
                updateDetailsPanel(selected);
            } else {
                closeDetailsPanel();
            }
        }
    } catch (error) {
        console.warn('Backend connection failed', error);
        setConnectionState(false);
        mockFallbackMode();
    }
}

function setConnectionState(isOnline) {
    connectionStatusEl.innerText = isOnline ? '● ONLINE' : '● OFFLINE';
    connectionStatusEl.classList.toggle('online', isOnline);
    connectionStatusEl.classList.toggle('offline', !isOnline);
}

function updateMap(flights) {
    flights.forEach(flight => {
        const id = flight.callsign;
        const position = Cesium.Cartesian3.fromDegrees(flight.currentLon, flight.currentLat, flight.altitude);
        const orientation = Cesium.Transforms.headingPitchRollQuaternion(position, new Cesium.HeadingPitchRoll(flight.heading, 0, 0));

        if (!flightEntities[id]) {
            flightEntities[id] = viewer.entities.add({
                id,
                position,
                orientation,
                model: { uri: 'models/Cesium_Air.glb', minimumPixelSize: 64, maximumScale: 20000 },
                label: {
                    text: id,
                    font: '12px "Inter", sans-serif',
                    showBackground: true,
                    pixelOffset: new Cesium.Cartesian2(0, -22)
                }
            });
        } else {
            const entity = flightEntities[id];
            entity.position = position;
            entity.orientation = orientation;
        }

        updateRouteEntities(flight);
    });
}

const handler = new Cesium.ScreenSpaceEventHandler(viewer.scene.canvas);
handler.setInputAction(function (click) {
    const pickedObject = viewer.scene.pick(click.position);
    if (Cesium.defined(pickedObject) && pickedObject.id && flightEntities[pickedObject.id.id]) {
        selectFlight(pickedObject.id.id, { flyTo: false });
    }
}, Cesium.ScreenSpaceEventType.LEFT_CLICK);

document.getElementById('btn-close-details').onclick = closeDetailsPanel;

function selectFlight(callsign, options = {}) {
    selectedFlightId = callsign;
    document.getElementById('flight-details-panel').classList.remove('hidden');
    highlightListSelection();
    updateRouteVisibility();

    const flight = latestFlights.find(f => f.callsign === callsign);
    if (flight) {
        updateDetailsPanel(flight);
        if (options.flyTo) {
            focusOnFlight(flight);
        }
    }
}

function focusOnFlight(flight) {
    viewer.camera.flyTo({
        destination: Cesium.Cartesian3.fromDegrees(flight.currentLon, flight.currentLat, Math.max(flight.altitude + 200000, 300000)),
        orientation: {
            heading: viewer.camera.heading,
            pitch: -0.8,
            roll: 0.0
        },
        duration: 1.2
    });
}

function closeDetailsPanel() {
    selectedFlightId = null;
    document.getElementById('flight-details-panel').classList.add('hidden');
    highlightListSelection();
    updateRouteVisibility();
}

function updateDetailsPanel(flight) {
    document.getElementById('detail-callsign').innerText = flight.callsign;
    document.getElementById('detail-from').innerText = flight.from;
    document.getElementById('detail-to').innerText = flight.to;
    document.getElementById('detail-status').innerText = flight.status;
    document.getElementById('detail-altitude').innerText = `${Math.round(flight.altitude)} m`;
    document.getElementById('detail-speed').innerText = `${Math.round(flight.speedMs)} m/s`;
    document.getElementById('detail-lat').innerText = flight.currentLat.toFixed(4);
    document.getElementById('detail-lon').innerText = flight.currentLon.toFixed(4);

    const currentPos = Cesium.Cartesian3.fromDegrees(flight.currentLon, flight.currentLat);
    const destPos = Cesium.Cartesian3.fromDegrees(flight.destLon, flight.destLat);
    const originPos = Cesium.Cartesian3.fromDegrees(flight.originLon, flight.originLat);

    const remainingDistance = Cesium.Cartesian3.distance(currentPos, destPos);
    const totalDistance = Cesium.Cartesian3.distance(originPos, destPos);
    const traveledDistance = Math.max(totalDistance - remainingDistance, 0);

    if (flight.speedMs > 0 && flight.status === 'ACTIVE') {
        const remainingSeconds = remainingDistance / flight.speedMs;
        const arrivalDate = new Date(Date.now() + remainingSeconds * 1000);
        document.getElementById('detail-arrival').innerText = arrivalDate.toLocaleTimeString();
    } else {
        document.getElementById('detail-arrival').innerText = '--:--';
    }

    document.getElementById('detail-distance').innerText = formatKilometers(totalDistance);
    document.getElementById('detail-remaining').innerText = formatKilometers(remainingDistance);
    document.getElementById('detail-traveled').innerText = formatKilometers(traveledDistance);
}

function renderFlightList(flights) {
    const filter = searchInputEl.value.trim().toLowerCase();
    const statusOrder = { ACTIVE: 0, WAITING: 1, LANDED: 2 };

    const filtered = flights
        .filter(f => {
            if (!filter) return true;
            return (
                f.callsign.toLowerCase().includes(filter) ||
                f.from.toLowerCase().includes(filter) ||
                f.to.toLowerCase().includes(filter)
            );
        })
        .sort((a, b) => {
            const statusDiff = (statusOrder[a.status] ?? 5) - (statusOrder[b.status] ?? 5);
            if (statusDiff !== 0) return statusDiff;
            return a.callsign.localeCompare(b.callsign);
        });

    flightListEl.innerHTML = '';

    filtered.forEach(flight => {
        const li = document.createElement('li');
        li.className = 'flight-row';
        li.dataset.id = flight.callsign;
        if (flight.callsign === selectedFlightId) {
            li.classList.add('selected');
        }

        const remainingDistance = Cesium.Cartesian3.distance(
            Cesium.Cartesian3.fromDegrees(flight.currentLon, flight.currentLat),
            Cesium.Cartesian3.fromDegrees(flight.destLon, flight.destLat)
        );

        li.innerHTML = `
            <div class="flight-title">
                <span>${flight.callsign}</span>
                <span class="status-chip ${flight.status.toLowerCase()}">${flight.status}</span>
            </div>
            <div class="flight-route">${flight.from} ➝ ${flight.to}</div>
            <div class="flight-meta">
                <span class="distance-chip">${formatKilometers(remainingDistance)} left</span>
                <span>${Math.round(flight.altitude)} m</span>
            </div>
        `;

        li.onclick = () => selectFlight(flight.callsign, { flyTo: true });
        flightListEl.appendChild(li);
    });
}

function highlightListSelection() {
    const rows = flightListEl.querySelectorAll('.flight-row');
    rows.forEach(row => {
        if (row.dataset.id === selectedFlightId) {
            row.classList.add('selected');
        } else {
            row.classList.remove('selected');
        }
    });
}

function mockFallbackMode() {
    if (latestFlights.length > 0) {
        return;
    }

    const demoFlights = [
        { callsign: 'DEMO-001', from: 'IST', to: 'LHR', originLat: 41.0, originLon: 28.9, destLat: 51.47, destLon: -0.45, currentLat: 41.2, currentLon: 29.1, altitude: 10000, heading: 0.5, speedMs: 240, status: 'ACTIVE' },
        { callsign: 'DEMO-002', from: 'CDG', to: 'JFK', originLat: 49.0, originLon: 2.55, destLat: 40.64, destLon: -73.78, currentLat: 48.0, currentLon: -5.0, altitude: 11000, heading: 1.1, speedMs: 250, status: 'ACTIVE' }
    ];

    updateMap(demoFlights);
    latestFlights = demoFlights;
    renderFlightList(latestFlights);
}

setInterval(fetchFromBackend, 200);

function createRouteEntities(id) {
    const traveled = viewer.entities.add({
        id: `${id}-route-traveled`,
        show: false,
        polyline: {
            positions: [],
            width: 3,
            material: new Cesium.PolylineGlowMaterialProperty({
                glowPower: 0.1,
                color: Cesium.Color.fromCssColorString('#00ff9d').withAlpha(0.85)
            })
        }
    });

    const remaining = viewer.entities.add({
        id: `${id}-route-remaining`,
        show: false,
        polyline: {
            positions: [],
            width: 2,
            material: new Cesium.PolylineDashMaterialProperty({
                color: Cesium.Color.fromCssColorString('#ffffff').withAlpha(0.7),
                gapColor: Cesium.Color.TRANSPARENT,
                dashLength: 32
            })
        }
    });

    return { traveled, remaining, hasRemainingLeg: true };
}

function updateRouteEntities(flight) {
    const id = flight.callsign;
    if (!flightRoutes[id]) {
        flightRoutes[id] = createRouteEntities(id);
    }

    const { traveled, remaining } = flightRoutes[id];

    const origin = [flight.originLon, flight.originLat, 0];
    const current = [flight.currentLon, flight.currentLat, Math.max(flight.altitude, 0)];
    const dest = [flight.destLon, flight.destLat, 0];

    traveled.polyline.positions = Cesium.Cartesian3.fromDegreesArrayHeights([...origin, ...current]);
    remaining.polyline.positions = Cesium.Cartesian3.fromDegreesArrayHeights([...current, ...dest]);

    const remainingDistance = Cesium.Cartesian3.distance(
        Cesium.Cartesian3.fromDegrees(flight.currentLon, flight.currentLat),
        Cesium.Cartesian3.fromDegrees(flight.destLon, flight.destLat)
    );

    const hasRemainingLeg = remainingDistance > 500;
    flightRoutes[id].hasRemainingLeg = hasRemainingLeg;
}

function updateRouteVisibility() {
    Object.entries(flightRoutes).forEach(([id, route]) => {
        const isSelected = selectedFlightId === id;
        const hasTraveledData = route.traveled.polyline.positions.length >= 6;
        route.traveled.show = isSelected && hasTraveledData;
        const hasRemainingData = route.remaining.polyline.positions.length >= 6;
        const allowRemaining = route.hasRemainingLeg ?? true;
        route.remaining.show = isSelected && hasRemainingData && allowRemaining;
    });
}

function formatKilometers(distanceMeters) {
    if (!Number.isFinite(distanceMeters)) {
        return '--';
    }
    const km = distanceMeters / 1000;
    if (km >= 1000) {
        return `${(km / 1000).toFixed(1)}k km`;
    }
    return `${km.toFixed(1)} km`;
}

fetchFromBackend();