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

const API_BASE_URL = (() => {
    const explicit = window.API_BASE_URL;
    if (typeof explicit === 'string' && explicit.trim().length > 0) {
        return explicit.trim().replace(/\/?$/, '');
    }

    const origin = window.location.origin;
    if (origin && origin.startsWith('http')) {
        return origin.replace(/\/?$/, '');
    }

    return 'http://localhost:5001';
})();
const connectionStatusEl = document.getElementById('connection-status');
const activeCountEl = document.getElementById('active-count');
const speedValueEl = document.getElementById('speed-val');
const flightListEl = document.getElementById('flight-list');
const searchInputEl = document.getElementById('search-input');
const detailCallsignEl = document.getElementById('detail-callsign');
const detailFromEl = document.getElementById('detail-from');
const detailToEl = document.getElementById('detail-to');
const detailStatusEl = document.getElementById('detail-status');
const detailAltitudeEl = document.getElementById('detail-altitude');
const detailSpeedEl = document.getElementById('detail-speed');
const detailLatEl = document.getElementById('detail-lat');
const detailLonEl = document.getElementById('detail-lon');
const detailArrivalEl = document.getElementById('detail-arrival');
const detailDistanceEl = document.getElementById('detail-distance');
const detailRemainingEl = document.getElementById('detail-remaining');
const detailTraveledEl = document.getElementById('detail-traveled');
const detailAircraftEl = document.getElementById('detail-aircraft');
const detailDepartureEl = document.getElementById('detail-departure');
const detailHeadingEl = document.getElementById('detail-heading');
const detailProgressEl = document.getElementById('detail-progress');
const planForm = document.getElementById('flight-plan-form');
const planningEnabled = Boolean(planForm);
const planCallsignInput = planningEnabled ? document.getElementById('plan-callsign') : null;
const planOriginSelect = planningEnabled ? document.getElementById('plan-origin') : null;
const planDestinationSelect = planningEnabled ? document.getElementById('plan-destination') : null;
const planAircraftSelect = planningEnabled ? document.getElementById('plan-aircraft') : null;
const planStartInput = planningEnabled ? document.getElementById('plan-start') : null;
const planSpeedInput = planningEnabled ? document.getElementById('plan-speed') : null;
const planStatusEl = planningEnabled ? document.getElementById('plan-status') : null;
const planSubmitBtn = planningEnabled ? document.getElementById('plan-submit') : null;
const planRefreshBtn = planningEnabled ? document.getElementById('plan-refresh') : null;

document.getElementById('btn-start').onclick = () => sendControl('start');
document.getElementById('btn-stop').onclick = () => sendControl('stop');
document.getElementById('btn-reset').onclick = () => sendControl('reset');

document.getElementById('speed-slider').oninput = (event) => {
    const val = Number(event.target.value);
    speedValueEl.innerText = `${val.toFixed(1)}x`;
    fetch(`${API_BASE_URL}/api/simulation/speed`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ multiplier: val })
    });
};

searchInputEl.addEventListener('input', () => renderFlightList(latestFlights));

if (planningEnabled) {
    planForm.addEventListener('submit', handlePlanSubmit);
    planRefreshBtn.addEventListener('click', () => loadPlanningLookups({ showToast: true }));
}

async function sendControl(action) {
    await fetch(`${API_BASE_URL}/api/simulation/${action}`, { method: 'POST' });
}

const flightEntities = {};
const flightRoutes = {};
let latestFlights = [];
let selectedFlightId = null;
let airportsCache = [];
let aircraftCache = [];
const baseTraveledColor = Cesium.Color.fromCssColorString('#ff4d4f');
const baseRemainingColor = Cesium.Color.fromCssColorString('#ffffff');

async function fetchFromBackend() {
    try {
        const response = await fetch(`${API_BASE_URL}/api/flights`);
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

    const currentIds = new Set(flights.map(f => f.callsign));
    Object.keys(flightEntities).forEach(id => {
        if (!currentIds.has(id)) {
            viewer.entities.remove(flightEntities[id]);
            delete flightEntities[id];
        }
    });

    Object.keys(flightRoutes).forEach(id => {
        if (!currentIds.has(id)) {
            viewer.entities.remove(flightRoutes[id].traveled);
            viewer.entities.remove(flightRoutes[id].remaining);
            delete flightRoutes[id];
        }
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
    if (!flight) {
        return;
    }

    detailCallsignEl.innerText = flight.callsign;
    detailFromEl.innerText = flight.from;
    detailToEl.innerText = flight.to;
    detailStatusEl.innerText = flight.status;
    detailAltitudeEl.innerText = `${Math.round(flight.altitude)} m`;
    detailSpeedEl.innerText = `${Math.round(flight.speedMs)} m/s`;
    detailLatEl.innerText = flight.currentLat.toFixed(4);
    detailLonEl.innerText = flight.currentLon.toFixed(4);

    if (detailAircraftEl) {
        const tail = flight.aircraftTail || '—';
        const model = flight.aircraftModel || '';
        detailAircraftEl.innerText = model ? `${tail} · ${model}` : tail;
    }

    if (detailDepartureEl) {
        const departure = new Date(flight.startTime || flight.start_time || Date.now());
        detailDepartureEl.innerText = Number.isNaN(departure.getTime())
            ? '--:--'
            : departure.toLocaleTimeString();
    }

    const currentPos = Cesium.Cartesian3.fromDegrees(flight.currentLon, flight.currentLat);
    const destPos = Cesium.Cartesian3.fromDegrees(flight.destLon, flight.destLat);
    const originPos = Cesium.Cartesian3.fromDegrees(flight.originLon, flight.originLat);

    const remainingDistance = Cesium.Cartesian3.distance(currentPos, destPos);
    const totalDistance = Cesium.Cartesian3.distance(originPos, destPos);
    const traveledDistance = Math.max(totalDistance - remainingDistance, 0);

    if (flight.speedMs > 0 && flight.status === 'ACTIVE') {
        const remainingSeconds = remainingDistance / flight.speedMs;
        const arrivalDate = new Date(Date.now() + remainingSeconds * 1000);
        detailArrivalEl.innerText = arrivalDate.toLocaleTimeString();
    } else {
        detailArrivalEl.innerText = '--:--';
    }

    detailDistanceEl.innerText = formatKilometers(totalDistance);
    detailRemainingEl.innerText = formatKilometers(remainingDistance);
    detailTraveledEl.innerText = formatKilometers(traveledDistance);

    if (detailHeadingEl) {
        const headingDegrees = ((flight.heading * 180) / Math.PI + 360) % 360;
        detailHeadingEl.innerText = `${headingDegrees.toFixed(0)}°`;
    }

    if (detailProgressEl) {
        detailProgressEl.innerText = `${Math.round(Math.min(Math.max(flight.progress ?? 0, 0), 1) * 100)}%`;
    }
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

    if (filtered.length === 0) {
        const emptyState = document.createElement('li');
        emptyState.className = 'flight-row empty';
        emptyState.innerHTML = `<div class="flight-route">No flights match the current filters.</div>`;
        flightListEl.appendChild(emptyState);
        return;
    }

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

function createRouteEntities(id) {
    const traveled = viewer.entities.add({
        id: `${id}-route-traveled`,
        show: false,
        polyline: {
            positions: [],
            width: 3,
            material: new Cesium.ColorMaterialProperty(baseTraveledColor.withAlpha(0.85))
        }
    });

    const remaining = viewer.entities.add({
        id: `${id}-route-remaining`,
        show: false,
        polyline: {
            positions: [],
            width: 2,
            material: new Cesium.PolylineDashMaterialProperty({
                color: baseRemainingColor.withAlpha(0.7),
                gapColor: Cesium.Color.TRANSPARENT,
                dashLength: 28
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

    flightRoutes[id].hasRemainingLeg = remainingDistance > 50;
}

function updateRouteVisibility() {
    Object.entries(flightRoutes).forEach(([id, route]) => {
        const isSelected = selectedFlightId === id;
        const hasTraveledData = route.traveled.polyline.positions.length >= 6;
        const hasRemainingData = route.remaining.polyline.positions.length >= 6;
        const allowRemaining = route.hasRemainingLeg ?? true;

        route.traveled.show = isSelected && hasTraveledData;
        route.remaining.show = isSelected && hasRemainingData && allowRemaining;
        route.traveled.polyline.width = isSelected ? 4 : 2.5;
        route.remaining.polyline.width = isSelected ? 3 : 2;

        if (route.traveled.polyline.material?.color) {
            route.traveled.polyline.material.color = baseTraveledColor.withAlpha(isSelected ? 0.95 : 0.0);
        }

        if (route.remaining.polyline.material?.color) {
            route.remaining.polyline.material.color = baseRemainingColor.withAlpha(isSelected ? 0.85 : 0.0);
        }
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

function setPlanStatus(message, tone) {
    if (!planningEnabled || !planStatusEl) {
        return;
    }

    planStatusEl.textContent = message || '';
    planStatusEl.classList.remove('success', 'error');
    if (tone === 'success') {
        planStatusEl.classList.add('success');
    } else if (tone === 'error') {
        planStatusEl.classList.add('error');
    }
}

function toLocalInputValue(date) {
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
    return local.toISOString().slice(0, 16);
}

function setDefaultPlanStart() {
    if (!planningEnabled || !planStartInput) {
        return;
    }

    const defaultDate = new Date(Date.now() + 2 * 60 * 1000);
    planStartInput.value = toLocalInputValue(defaultDate);
}

function populateAirportSelect(selectEl, airports) {
    const previous = selectEl.value;
    selectEl.innerHTML = '';

    airports
        .slice()
        .sort((a, b) => a.name.localeCompare(b.name))
        .forEach(airport => {
            const option = document.createElement('option');
            option.value = airport.code;
            option.textContent = `${airport.code} — ${airport.name}`;
            selectEl.appendChild(option);
        });

    if (airports.length > 0) {
        selectEl.value = airports.some(a => a.code === previous) ? previous : airports[0].code;
    }
}

function populateAircraftSelect(aircraft) {
    const previous = planAircraftSelect.value;
    planAircraftSelect.innerHTML = '';

    aircraft
        .slice()
        .sort((a, b) => a.tailNumber.localeCompare(b.tailNumber))
        .forEach(item => {
            const option = document.createElement('option');
            option.value = item.tailNumber;
            option.textContent = `${item.tailNumber} — ${item.model}`;
            planAircraftSelect.appendChild(option);
        });

    if (aircraft.length > 0) {
        planAircraftSelect.value = aircraft.some(a => a.tailNumber === previous) ? previous : aircraft[0].tailNumber;
    }
}

async function loadPlanningLookups(options = {}) {
    if (!planningEnabled) {
        return;
    }

    try {
        planSubmitBtn.disabled = true;
        planRefreshBtn.disabled = true;

        const [airportsRes, aircraftRes] = await Promise.all([
            fetch(`${API_BASE_URL}/api/flightplans/airports`),
            fetch(`${API_BASE_URL}/api/flightplans/aircraft?availableOnly=true`)
        ]);

        if (!airportsRes.ok || !aircraftRes.ok) {
            throw new Error('Planner lookup failed');
        }

        airportsCache = await airportsRes.json();
        aircraftCache = await aircraftRes.json();

        populateAirportSelect(planOriginSelect, airportsCache);
        populateAirportSelect(planDestinationSelect, airportsCache);
        populateAircraftSelect(aircraftCache);

        const hasAircraft = aircraftCache.length > 0;
        planSubmitBtn.disabled = !hasAircraft;
        if (!hasAircraft) {
            setPlanStatus('No available aircraft right now.', 'error');
        } else if (options.showToast) {
            setPlanStatus('Planning data refreshed.', 'success');
        }
    } catch (error) {
        console.error('Failed to load planning data', error);
        setPlanStatus('Cannot load planning data. Try again.', 'error');
        planSubmitBtn.disabled = true;
    } finally {
        planRefreshBtn.disabled = false;
    }
}

async function handlePlanSubmit(event) {
    if (!planningEnabled) {
        return;
    }

    event.preventDefault();

    if (!planForm.checkValidity()) {
        planForm.reportValidity();
        return;
    }

    const origin = planOriginSelect.value;
    const destination = planDestinationSelect.value;

    if (!origin || !destination) {
        setPlanStatus('Select origin and destination airports.', 'error');
        return;
    }

    if (origin === destination) {
        setPlanStatus('Origin and destination must be different.', 'error');
        return;
    }

    if (!planAircraftSelect.value) {
        setPlanStatus('No available aircraft selected.', 'error');
        return;
    }

    if (!planStartInput.value) {
        setPlanStatus('Pick a planned departure time.', 'error');
        return;
    }

    const startDate = new Date(planStartInput.value);
    if (Number.isNaN(startDate.getTime())) {
        setPlanStatus('Departure time is invalid.', 'error');
        return;
    }

    const payload = {
        callsign: planCallsignInput.value.trim().toUpperCase(),
        aircraftTail: planAircraftSelect.value,
        originCode: origin,
        destinationCode: destination,
        startTimeUtc: startDate.toISOString()
    };

    const plannedSpeed = Number(planSpeedInput.value);
    if (Number.isFinite(plannedSpeed)) {
        payload.plannedSpeedMs = plannedSpeed;
    }

    if (!payload.callsign) {
        setPlanStatus('Enter a valid callsign.', 'error');
        return;
    }

    try {
        setPlanStatus('Creating flight plan…', undefined);
        planSubmitBtn.disabled = true;
        planRefreshBtn.disabled = true;

        const response = await fetch(`${API_BASE_URL}/api/flightplans`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            let message = 'Failed to create flight plan.';
            try {
                const errorBody = await response.json();
                if (errorBody?.message) {
                    message = errorBody.message;
                }
            } catch (parseError) {
                console.warn('Unable to parse error payload', parseError);
            }
            throw new Error(message);
        }

        const created = await response.json();
        setPlanStatus(`Flight ${created.callsign} scheduled.`, 'success');

        planForm.reset();
        setDefaultPlanStart();

        await loadPlanningLookups();
        await fetchFromBackend();
        planCallsignInput.focus();
    } catch (error) {
        setPlanStatus(error.message, 'error');
    } finally {
        planSubmitBtn.disabled = planAircraftSelect.options.length === 0;
        planRefreshBtn.disabled = false;
    }
}

if (planningEnabled) {
    setDefaultPlanStart();
    loadPlanningLookups();
}
fetchFromBackend();
setInterval(fetchFromBackend, 200);