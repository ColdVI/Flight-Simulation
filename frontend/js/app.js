let viewer; // Declared but initialized later

function initializeApp() {
    // Ensure Cesium is loaded
    if (typeof Cesium === 'undefined') {
        console.error('Cesium library failed to load');
        setTimeout(initializeApp, 100); // Retry in 100ms
        return;
    }

    viewer = new Cesium.Viewer('cesiumContainer', {
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

    startApp();
}

function startApp() {
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
const speedSliderEl = document.getElementById('speed-slider');
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
const detailDurationEl = document.getElementById('detail-duration');
const detailMaxSpeedEl = document.getElementById('detail-max-speed');
const detailAvgSpeedEl = document.getElementById('detail-avg-speed');
const detailMaxAltitudeEl = document.getElementById('detail-max-altitude');
const detailAvgAltitudeEl = document.getElementById('detail-avg-altitude');
const detailTotalDistanceEl = document.getElementById('detail-total-distance');
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
const sidebarTabs = document.querySelectorAll('[data-sidebar-tab]');
const sidebarPanels = document.querySelectorAll('[data-sidebar-panel]');
const controlsStatusEl = document.getElementById('controls-status');

if (sidebarTabs.length > 0 && sidebarPanels.length > 0) {
    const setSidebarPanel = (panelName) => {
        sidebarTabs.forEach(tab => {
            const isActive = tab.dataset.sidebarTab === panelName;
            tab.classList.toggle('is-active', isActive);
        });

        sidebarPanels.forEach(panel => {
            const isActive = panel.dataset.sidebarPanel === panelName;
            panel.classList.toggle('is-active', isActive);
        });
    };

    sidebarTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            setSidebarPanel(tab.dataset.sidebarTab);
        });
    });

    const defaultPanel = document.querySelector('.sidebar-tab.is-active')?.dataset.sidebarTab
        ?? sidebarTabs[0].dataset.sidebarTab;
    setSidebarPanel(defaultPanel);
}

// Flight filter handling
const filterButtons = document.querySelectorAll('.filter-btn');
filterButtons.forEach(btn => {
    btn.addEventListener('click', () => {
        filterButtons.forEach(b => b.classList.remove('is-active'));
        btn.classList.add('is-active');
        currentFlightFilter = btn.dataset.filter;
        renderFlightList(latestFlights);
    });
});

document.getElementById('btn-start').onclick = () => sendControl('start');
document.getElementById('btn-stop').onclick = () => sendControl('stop');
document.getElementById('btn-reset').onclick = () => sendControl('reset');

let speedUpdateTimer = null;
if (speedSliderEl) {
    speedSliderEl.addEventListener('input', (event) => {
        const value = Number(event.target.value);
        speedValueEl.innerText = `${value.toFixed(1)}x`;
        scheduleSpeedUpdate(value);
    });
}

searchInputEl.addEventListener('input', () => renderFlightList(latestFlights));

if (planningEnabled) {
    planForm.addEventListener('submit', handlePlanSubmit);
    planRefreshBtn.addEventListener('click', () => loadPlanningLookups({ showToast: true }));
}

setControlsStatus('Standing by');

function setControlsStatus(message, tone) {
    if (!controlsStatusEl) {
        return;
    }

    controlsStatusEl.textContent = message ?? '';
    controlsStatusEl.classList.remove('success', 'error');
    if (tone === 'success') {
        controlsStatusEl.classList.add('success');
    } else if (tone === 'error') {
        controlsStatusEl.classList.add('error');
    }
}

async function sendControl(action) {
    try {
        setControlsStatus(`Sending ${action} command…`);
        const response = await fetch(`${API_BASE_URL}/api/simulation/${action}`, { method: 'POST' });
        if (!response.ok) {
            throw new Error(`Simulation ${action} failed with status ${response.status}`);
        }

        let message = '';
        try {
            const payload = await response.json();
            message = payload?.message ?? '';
        } catch (parseError) {
            message = '';
        }

        if (!message) {
            message = action === 'start'
                ? 'Simulation running'
                : action === 'stop'
                    ? 'Simulation paused'
                    : action === 'reset'
                        ? 'Simulation reset'
                        : 'Command acknowledged';
        }

        setControlsStatus(message, 'success');
    } catch (error) {
        console.error('Simulation control failed', error);
        setControlsStatus('Command failed. Check the service logs.', 'error');
    }
}

function scheduleSpeedUpdate(multiplier) {
    if (speedUpdateTimer) {
        clearTimeout(speedUpdateTimer);
    }
    speedUpdateTimer = setTimeout(() => {
        updateSimulationSpeed(multiplier);
    }, 250);
}

async function updateSimulationSpeed(multiplier) {
    try {
        setControlsStatus(`Updating speed to ${multiplier.toFixed(1)}x…`);
        const response = await fetch(`${API_BASE_URL}/api/simulation/speed`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ multiplier })
        });

        if (!response.ok) {
            throw new Error(`Speed update failed (${response.status})`);
        }

        setControlsStatus(`Speed set to ${multiplier.toFixed(1)}x`, 'success');
    } catch (error) {
        console.error('Failed to adjust simulation speed', error);
        setControlsStatus('Could not update speed.', 'error');
    }
}

const flightEntities = {};
const flightRoutes = {};
let latestFlights = [];
let selectedFlightId = null;
let airportsCache = [];
let aircraftCache = [];
let currentFlightFilter = 'all'; // Track current filter
const baseTraveledColor = Cesium.Color.fromCssColorString('#ff4d4f');
const baseRemainingColor = Cesium.Color.fromCssColorString('#ffffff');
const POSITION_HISTORY_SECONDS = 120;
let clockInitialised = false;

// Haversine formula for great-circle distance between two lat/lon points
function calculateGreatCircleDistance(lat1, lon1, lat2, lon2) {
    const R = 6371000; // Earth's radius in meters
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
              Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
              Math.sin(dLon / 2) * Math.sin(dLon / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    return R * c; // distance in meters
}

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
    const sampleTime = Cesium.JulianDate.now();

    if (!clockInitialised) {
        viewer.clock.startTime = Cesium.JulianDate.clone(sampleTime);
        viewer.clock.currentTime = Cesium.JulianDate.clone(sampleTime);
        viewer.clock.stopTime = Cesium.JulianDate.addDays(sampleTime, 1, new Cesium.JulianDate());
        viewer.clock.clockRange = Cesium.ClockRange.UNBOUNDED;
        viewer.clock.shouldAnimate = true;
        clockInitialised = true;
    }

    flights.forEach(flight => {
        const id = flight.callsign;
        const position = Cesium.Cartesian3.fromDegrees(flight.currentLon, flight.currentLat, flight.altitude);

        if (!flightEntities[id]) {
            const positionProperty = new Cesium.SampledPositionProperty();
            positionProperty.setInterpolationOptions({
                interpolationDegree: 1,
                interpolationAlgorithm: Cesium.LinearApproximation
            });

            const entity = viewer.entities.add({
                id,
                position: positionProperty,
                orientation: new Cesium.VelocityOrientationProperty(positionProperty),
                model: { uri: 'models/Cesium_Air.glb', minimumPixelSize: 64, maximumScale: 20000 },
                label: {
                    text: id,
                    font: '12px "Inter", sans-serif',
                    showBackground: true,
                    pixelOffset: new Cesium.Cartesian2(0, -22)
                }
            });

            flightEntities[id] = {
                entity,
                positionProperty
            };
        }

        const { positionProperty } = flightEntities[id];
        positionProperty.addSample(sampleTime, position);

        // Remove old samples if the method exists (may not be available in all Cesium versions)
        try {
            const cutoff = Cesium.JulianDate.addSeconds(sampleTime, -POSITION_HISTORY_SECONDS, new Cesium.JulianDate());
            if (typeof positionProperty.removeSamplesBefore === 'function') {
                positionProperty.removeSamplesBefore(cutoff);
            }
        } catch (err) {
            // Ignore errors from sample removal - not critical
        }

        updateRouteEntities(flight);
    });

    const currentIds = new Set(flights.map(f => f.callsign));
    Object.keys(flightEntities).forEach(id => {
        if (!currentIds.has(id)) {
            viewer.entities.remove(flightEntities[id].entity);
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
    if (!Cesium.defined(pickedObject) || !pickedObject.id) {
        return;
    }

    const picked = pickedObject.id;
    const entityId = typeof picked === 'string' ? picked : picked.id;
    if (entityId && flightEntities[entityId]) {
        selectFlight(entityId, { flyTo: false });
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
    // Get the actual entity position from the rendered object
    const entity = flightEntities[flight.callsign]?.entity;
    if (!entity) {
        return;
    }

    // Calculate a good zoom distance (roughly 3x the flight altitude for perspective)
    const zoomAltitude = Math.max(flight.altitude * 3, 50000);

    // Fly to the entity's current position with smooth camera movement
    viewer.camera.flyTo({
        destination: Cesium.Cartesian3.fromDegrees(flight.currentLon, flight.currentLat, zoomAltitude),
        orientation: {
            heading: viewer.camera.heading,
            pitch: -60 * Math.PI / 180, // Convert degrees to radians for better view angle
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

    // Use haversine formula for accurate great-circle distances
    const remainingDistance = calculateGreatCircleDistance(
        flight.currentLat, flight.currentLon,
        flight.destLat, flight.destLon
    );
    const totalDistance = calculateGreatCircleDistance(
        flight.originLat, flight.originLon,
        flight.destLat, flight.destLon
    );
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

    // Update flight statistics/report
    if (detailDurationEl && flight.startTime) {
        const now = new Date();
        const startTime = new Date(flight.startTime);
        const durationMs = now.getTime() - startTime.getTime();
        const durationMins = Math.floor(durationMs / 60000);
        detailDurationEl.innerText = durationMins > 0 ? `${durationMins} min` : '-- min';
    }

    if (detailMaxSpeedEl) {
        detailMaxSpeedEl.innerText = flight.maxSpeed ? `${Math.round(flight.maxSpeed)} m/s` : '-- m/s';
    }

    if (detailAvgSpeedEl) {
        detailAvgSpeedEl.innerText = flight.averageSpeed ? `${Math.round(flight.averageSpeed)} m/s` : '-- m/s';
    }

    if (detailMaxAltitudeEl) {
        detailMaxAltitudeEl.innerText = flight.maxAltitude ? `${Math.round(flight.maxAltitude)} m` : '-- m';
    }

    if (detailAvgAltitudeEl) {
        detailAvgAltitudeEl.innerText = flight.averageAltitude ? `${Math.round(flight.averageAltitude)} m` : '-- m';
    }

    if (detailTotalDistanceEl) {
        detailTotalDistanceEl.innerText = flight.totalDistance ? formatKilometers(flight.totalDistance) : '-- km';
    }
}

function renderFlightList(flights) {
    const filter = searchInputEl.value.trim().toLowerCase();
    const statusOrder = { ACTIVE: 0, WAITING: 1, LANDED: 2 };

    const filtered = flights
        .filter(f => {
            // Apply status filter
            if (currentFlightFilter !== 'all' && f.status !== currentFlightFilter) {
                return false;
            }
            // Apply search filter
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

        const remainingDistance = calculateGreatCircleDistance(
            flight.currentLat, flight.currentLon,
            flight.destLat, flight.destLon
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

    return {
        traveled,
        remaining,
        hasRemainingLeg: true,
        traveledPositions: [],
        remainingPositions: []
    };
}

function updateRouteEntities(flight) {
    const id = flight.callsign;
    if (!flightRoutes[id]) {
        flightRoutes[id] = createRouteEntities(id);
    }

    const { traveled, remaining } = flightRoutes[id];

    // Helper for Great Circle interpolation
    function getGreatCirclePoint(lat1, lon1, lat2, lon2, t) {
        const toRad = Math.PI / 180;
        const toDeg = 180 / Math.PI;
        
        const phi1 = lat1 * toRad;
        const lam1 = lon1 * toRad;
        const phi2 = lat2 * toRad;
        const lam2 = lon2 * toRad;

        const dPhi = phi2 - phi1;
        const dLam = lam2 - lam1;

        const a = Math.sin(dPhi / 2) ** 2 +
                  Math.cos(phi1) * Math.cos(phi2) * Math.sin(dLam / 2) ** 2;
        const delta = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));

        if (delta === 0) return { lat: lat2, lon: lon2 };

        const A = Math.sin((1 - t) * delta) / Math.sin(delta);
        const B = Math.sin(t * delta) / Math.sin(delta);

        const x = A * Math.cos(phi1) * Math.cos(lam1) + B * Math.cos(phi2) * Math.cos(lam2);
        const y = A * Math.cos(phi1) * Math.sin(lam1) + B * Math.cos(phi2) * Math.sin(lam2);
        const z = A * Math.sin(phi1) + B * Math.sin(phi2);

        const newLat = Math.atan2(z, Math.sqrt(x * x + y * y)) * toDeg;
        const newLon = Math.atan2(y, x) * toDeg;

        return { lat: newLat, lon: newLon };
    }

    // Traveled route: from origin to current position
    const traveledArray = [];
    const numSteps = 20; // More steps for smoother curve
    
    // We need to interpolate from Origin to Current. 
    // However, "Current" is already on the Great Circle path from Origin to Dest.
    // So we can just interpolate from Origin to Current directly using GC.
    
    for (let i = 0; i <= numSteps; i++) {
        const t = i / numSteps;
        const point = getGreatCirclePoint(flight.originLat, flight.originLon, flight.currentLat, flight.currentLon, t);
        
        // Altitude profile for visualization
        // We can approximate the altitude based on the total progress of the flight
        // But for the "traveled" segment, we just want to connect O to C.
        // The backend calculates altitude based on total progress.
        // Let's just linearly interpolate altitude for the visualization segment to keep it simple and connected.
        const alt = flight.altitude * t; 
        
        traveledArray.push(point.lon, point.lat, alt);
    }

    // Remaining route: from current to destination
    const remainingArray = [];
    for (let i = 0; i <= numSteps; i++) {
        const t = i / numSteps;
        const point = getGreatCirclePoint(flight.currentLat, flight.currentLon, flight.destLat, flight.destLon, t);
        
        const alt = flight.altitude * (1 - t); // Linear descent for visualization
        remainingArray.push(point.lon, point.lat, alt);
    }

    const traveledPositions = Cesium.Cartesian3.fromDegreesArrayHeights(traveledArray);
    const remainingPositions = Cesium.Cartesian3.fromDegreesArrayHeights(remainingArray);

    traveled.polyline.positions = traveledPositions;
    remaining.polyline.positions = remainingPositions;

    flightRoutes[id].traveledPositions = traveledPositions;
    flightRoutes[id].remainingPositions = remainingPositions;

    const remainingDistance = calculateGreatCircleDistance(
        flight.currentLat, flight.currentLon,
        flight.destLat, flight.destLon
    );

    flightRoutes[id].hasRemainingLeg = remainingDistance > 50;
}

function updateRouteVisibility() {
    Object.entries(flightRoutes).forEach(([id, route]) => {
        const isSelected = selectedFlightId === id;
        const traveledPositions = route.traveledPositions || [];
        const remainingPositions = route.remainingPositions || [];
        const hasTraveledData = traveledPositions.length >= 6;
        const hasRemainingData = remainingPositions.length >= 6;
        const allowRemaining = route.hasRemainingLeg ?? true;

        // Only show routes for the selected flight
        route.traveled.show = isSelected && hasTraveledData;
        route.remaining.show = isSelected && hasRemainingData && allowRemaining;
        
        // Thicker lines for selected flight
        route.traveled.polyline.width = 4;
        route.remaining.polyline.width = 3;

        // High opacity when selected
        route.traveled.polyline.material = new Cesium.ColorMaterialProperty(
            baseTraveledColor.withAlpha(0.95)
        );

        route.remaining.polyline.material = new Cesium.PolylineDashMaterialProperty({
            color: baseRemainingColor.withAlpha(0.95),
            gapColor: Cesium.Color.TRANSPARENT,
            dashLength: 28
        });
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
}

// Initialize the app when the page loads
console.log('App script loaded, document.readyState:', document.readyState);
if (document.readyState === 'loading') {
    console.log('DOM still loading, waiting for DOMContentLoaded...');
    document.addEventListener('DOMContentLoaded', () => {
        console.log('DOMContentLoaded fired, initializing app...');
        initializeApp();
    });
} else {
    // DOM is already loaded (e.g., in some edge cases)
    console.log('DOM already loaded, initializing app immediately...');
    initializeApp();
}