-- Schema and seed data for the Flight Simulation backend
BEGIN;

CREATE TABLE IF NOT EXISTS airports (
    code TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    city TEXT NULL,
    country TEXT NULL,
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL
);

CREATE TABLE IF NOT EXISTS aircraft (
    tail_number TEXT PRIMARY KEY,
    model TEXT NOT NULL,
    manufacturer TEXT NULL,
    cruise_speed_ms DOUBLE PRECISION NOT NULL
);

CREATE TABLE IF NOT EXISTS flight_plans (
    callsign TEXT PRIMARY KEY,
    aircraft_tail TEXT NOT NULL REFERENCES aircraft(tail_number),
    origin_code TEXT NOT NULL REFERENCES airports(code),
    destination_code TEXT NOT NULL REFERENCES airports(code),
    planned_speed_ms DOUBLE PRECISION NOT NULL,
    start_offset_seconds INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS flight_states (
    callsign TEXT PRIMARY KEY REFERENCES flight_plans(callsign) ON DELETE CASCADE,
    status TEXT NOT NULL,
    current_lat DOUBLE PRECISION NOT NULL,
    current_lon DOUBLE PRECISION NOT NULL,
    altitude DOUBLE PRECISION NOT NULL,
    heading DOUBLE PRECISION NOT NULL,
    speed_ms DOUBLE PRECISION NOT NULL,
    progress DOUBLE PRECISION NOT NULL,
    start_time TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO airports (code, name, city, country, latitude, longitude) VALUES
    ('IST', 'Istanbul Airport', 'Istanbul', 'Turkey', 40.976, 28.814),
    ('JFK', 'John F. Kennedy International Airport', 'New York', 'United States', 40.641, -73.778),
    ('LHR', 'London Heathrow Airport', 'London', 'United Kingdom', 51.47, -0.454),
    ('DXB', 'Dubai International Airport', 'Dubai', 'United Arab Emirates', 25.253, 55.365),
    ('FRA', 'Frankfurt Airport', 'Frankfurt', 'Germany', 50.037, 8.562),
    ('HND', 'Tokyo Haneda Airport', 'Tokyo', 'Japan', 35.549, 139.779),
    ('CDG', 'Paris Charles de Gaulle Airport', 'Paris', 'France', 49.009, 2.547),
    ('DOH', 'Hamad International Airport', 'Doha', 'Qatar', 25.273, 51.608),
    ('SIN', 'Singapore Changi Airport', 'Singapore', 'Singapore', 1.364, 103.991)
ON CONFLICT (code) DO NOTHING;

INSERT INTO aircraft (tail_number, model, manufacturer, cruise_speed_ms) VALUES
    ('TC-LGA', 'Airbus A350-900', 'Airbus', 250),
    ('G-XLEA', 'Airbus A380-800', 'Airbus', 240),
    ('D-ABYA', 'Boeing 747-8I', 'Boeing', 260),
    ('F-HPJE', 'Airbus A380-800', 'Airbus', 245),
    ('A7-BAA', 'Boeing 777-300ER', 'Boeing', 255),
    ('A6-EUA', 'Airbus A380-800', 'Airbus', 265),
    ('9V-SMA', 'Airbus A350-900', 'Airbus', 250),
    ('TC-JJZ', 'Boeing 737-900', 'Boeing', 230),
    ('N735AT', 'Boeing 777-200ER', 'Boeing', 240),
    ('JA735J', 'Boeing 777-300ER', 'Boeing', 270)
ON CONFLICT (tail_number) DO NOTHING;

INSERT INTO flight_plans (callsign, aircraft_tail, origin_code, destination_code, planned_speed_ms, start_offset_seconds)
VALUES
    ('THY-101', 'TC-LGA', 'IST', 'JFK', 250, 0),
    ('BAW-256', 'G-XLEA', 'LHR', 'DXB', 240, 5),
    ('DLH-400', 'D-ABYA', 'FRA', 'HND', 260, 10),
    ('AFR-006', 'F-HPJE', 'CDG', 'JFK', 245, 2),
    ('QTR-001', 'A7-BAA', 'DOH', 'LHR', 255, 8),
    ('UAE-203', 'A6-EUA', 'DXB', 'JFK', 265, 15),
    ('SIA-308', '9V-SMA', 'SIN', 'LHR', 250, 20),
    ('THY-001', 'TC-JJZ', 'IST', 'LHR', 230, 3),
    ('AAL-100', 'N735AT', 'JFK', 'LHR', 240, 12),
    ('JAL-005', 'JA735J', 'HND', 'JFK', 270, 25)
ON CONFLICT (callsign) DO NOTHING;

INSERT INTO flight_states (callsign, status, current_lat, current_lon, altitude, heading, speed_ms, progress, start_time)
SELECT
    fp.callsign,
    'WAITING' AS status,
    origin.latitude AS current_lat,
    origin.longitude AS current_lon,
    0 AS altitude,
    ATAN2(dest.longitude - origin.longitude, dest.latitude - origin.latitude) AS heading,
    fp.planned_speed_ms AS speed_ms,
    0 AS progress,
    NOW() + (fp.start_offset_seconds || ' seconds')::interval AS start_time
FROM flight_plans fp
JOIN airports origin ON origin.code = fp.origin_code
JOIN airports dest ON dest.code = fp.destination_code
ON CONFLICT (callsign) DO NOTHING;

COMMIT;
