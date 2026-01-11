# Flight-Simulation

This project delivers a modern live-flight radar experience by coupling a PostgreSQL-backed ASP.NET Core API with an immersive CesiumJS frontend. A background simulation loop keeps every aircraft in motion, while the UI highlights the selected flight with route lines and detailed telemetry.

## Architecture Overview

- **Backend** – `backend/FlightRadarAPI`
	- ASP.NET Core Web API targeting `net10.0`, persisting flight state in PostgreSQL.
	- `SimulationService` runs in the background, advances each aircraft, and exposes REST control endpoints.
	- Also serves the static assets in the `frontend/` folder.
- **Frontend** – `frontend`
	- Local CesiumJS 1.109 distribution plus a custom radar-style interface.
	- Flight list with search and status badges, map overlays for routes, speed slider, start/stop/reset controls, and an expanded detail sidebar.
- **Data Seed** – `backend/FlightRadarAPI/Data/flights.json`
	- Loaded automatically into PostgreSQL the first time the app starts.

## Requirements

- .NET SDK 10.0 (or a compatible preview build)
- PostgreSQL 14 or newer (local or remote)
- `psql` client or another tool to create the database

## Database Setup

1. Ensure PostgreSQL is running and reachable.
2. Create the database (defaults shown below):

	 ```bash
	 createdb flight_sim
	 ```

3. Update the connection string in `backend/FlightRadarAPI/appsettings.json` (or provide it via environment variable):

	 ```json
	 "ConnectionStrings": {
		 "Postgres": "Host=localhost;Port=5432;Database=flight_sim;Username=postgres;Password=postgres"
	 }
	 ```

	 To override via environment variable, use the `ConnectionStrings__Postgres="Host=..."` syntax when launching the app.

4. On first launch the application will create the `flights` table and seed it using `Data/flights.json`.

## Run the Project

```bash
cd backend/FlightRadarAPI
dotnet restore
dotnet run
```

- The API and static frontend will be available at `http://localhost:5001`.
- Open that address in a browser to explore the interactive flight radar UI.
- All telemetry is fetched from `http://localhost:5001/api/flights`.

## Frontend Highlights

- Radar-style top bar, collapsible sidebar, and floating detail panel.
- Flight list shows status (ACTIVE / WAITING / LANDED), remaining distance, altitude, and supports quick search.
- Selecting a flight highlights both flown and remaining segments directly on the globe.
- Detail panel exposes total/traveled/remaining distance, ETA, coordinates, altitude, and speed.
- Speed slider and control buttons call the REST endpoints to steer the simulation.

## API Overview

- `GET /api/flights` – Returns the latest state for every flight.
- `POST /api/simulation/start|stop|reset` – Controls the simulation loop.
- `POST /api/simulation/speed` – Accepts `{ "multiplier": 1.5 }` to change the time scale.

## Troubleshooting

- **Connection status stays OFFLINE** – Verify PostgreSQL is reachable and `dotnet run` did not report connection issues.
- **Insufficient privileges** – Ensure the configured user can create tables and perform `INSERT/UPDATE` operations in the `flight_sim` database.
- **Port conflict** – If `http://localhost:5001` is already in use, adjust the `UseUrls` call in `Program.cs` and update the frontend fetch URLs accordingly.

## Extending the Project

- Add new sample routes to `Data/flights.json` and restart the backend to reseed the simulator.
- Enhance the Cesium overlay or telemetry widgets by expanding the logic inside `frontend/js/app.js` (for example, historic trails or additional analytics).

Need a hand deploying or extending the project? Open an issue or drop a note—happy to help.
