using System.Text.Json;
using FlightRadarAPI.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FlightRadarAPI.Data
{
    public interface IFlightRepository
    {
        Task InitializeAsync(CancellationToken cancellationToken);
        Task<List<Flight>> GetAllAsync(CancellationToken cancellationToken);
        Task BulkUpsertAsync(IEnumerable<Flight> flights, CancellationToken cancellationToken);
        Task ResetFlightsAsync(IEnumerable<Flight> flights, CancellationToken cancellationToken);
        Task<List<AirportSummary>> GetAirportsAsync(CancellationToken cancellationToken);
        Task<List<AircraftSummary>> GetAircraftAsync(CancellationToken cancellationToken);
        Task<List<FlightPlanSummary>> GetFlightPlansAsync(CancellationToken cancellationToken);
        Task<FlightPlanSummary?> GetFlightPlanAsync(string callsign, CancellationToken cancellationToken);
        Task<FlightPlanSummary?> CreateFlightPlanAsync(FlightPlanRequest request, CancellationToken cancellationToken);
        Task<Flight?> GetFlightAsync(string callsign, CancellationToken cancellationToken);
    }

    public class FlightRepository : IFlightRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<FlightRepository> _logger;
        private readonly IWebHostEnvironment _environment;

        private const string FlightSelectSql = @"
SELECT fp.callsign, fp.origin_code, fp.destination_code,
       origin.name AS origin_name, origin.latitude AS origin_lat, origin.longitude AS origin_lon,
       dest.name AS dest_name, dest.latitude AS dest_lat, dest.longitude AS dest_lon,
       fp.planned_speed_ms, fp.start_offset_seconds,
       fs.start_time, fs.status, fs.current_lat, fs.current_lon,
       fs.altitude, fs.heading, fs.speed_ms, fs.progress,
       ac.tail_number, ac.model, ac.manufacturer
FROM flight_plans fp
JOIN flight_states fs ON fp.callsign = fs.callsign
JOIN airports origin ON fp.origin_code = origin.code
JOIN airports dest ON fp.destination_code = dest.code
JOIN aircraft ac ON fp.aircraft_tail = ac.tail_number
";

        public FlightRepository(IConfiguration configuration, ILogger<FlightRepository> logger, IWebHostEnvironment environment)
        {
            _connectionString = configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
            _logger = logger;
            _environment = environment;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await EnsureSchemaAsync(connection, cancellationToken);

            var airports = await EnsureAirportsAsync(connection, cancellationToken);
            var aircraft = await EnsureAircraftAsync(connection, cancellationToken);
            await EnsureFlightPlansAsync(connection, airports, aircraft, cancellationToken);
        }

        public async Task<List<Flight>> GetAllAsync(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(FlightSelectSql + "ORDER BY fp.callsign;", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var flights = new List<Flight>();
            while (await reader.ReadAsync(cancellationToken))
            {
                flights.Add(ReadFlight(reader));
            }

            return flights;
        }

        public async Task<Flight?> GetFlightAsync(string callsign, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(callsign))
            {
                return null;
            }

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(FlightSelectSql + "WHERE fp.callsign = @callsign;", connection);
            command.Parameters.AddWithValue("@callsign", callsign);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ReadFlight(reader);
            }

            return null;
        }

        public async Task<List<AirportSummary>> GetAirportsAsync(CancellationToken cancellationToken)
        {
            const string sql = "SELECT code, name, city, country, latitude, longitude FROM airports ORDER BY name;";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var airports = new List<AirportSummary>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                airports.Add(new AirportSummary
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    City = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Country = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Latitude = reader.GetDouble(4),
                    Longitude = reader.GetDouble(5)
                });
            }

            return airports;
        }

        public async Task<List<AircraftSummary>> GetAircraftAsync(CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT ac.tail_number, ac.model, ac.manufacturer, ac.cruise_speed_ms,
       NOT EXISTS (
           SELECT 1
           FROM flight_plans fp
           JOIN flight_states fs ON fp.callsign = fs.callsign
           WHERE fp.aircraft_tail = ac.tail_number
             AND fs.status IN ('ACTIVE', 'WAITING')
       ) AS is_available
FROM aircraft ac
ORDER BY ac.tail_number;";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var aircraft = new List<AircraftSummary>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                aircraft.Add(new AircraftSummary
                {
                    TailNumber = reader.GetString(0),
                    Model = reader.GetString(1),
                    Manufacturer = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CruiseSpeedMs = reader.GetDouble(3),
                    IsAvailable = reader.GetBoolean(4)
                });
            }

            return aircraft;
        }

        public async Task<List<FlightPlanSummary>> GetFlightPlansAsync(CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT fp.callsign, fp.aircraft_tail, fp.origin_code, fp.destination_code,
       fp.planned_speed_ms, fs.start_time, fs.status, fs.progress
FROM flight_plans fp
JOIN flight_states fs ON fp.callsign = fs.callsign
ORDER BY fs.start_time;";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var plans = new List<FlightPlanSummary>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                plans.Add(new FlightPlanSummary
                {
                    Callsign = reader.GetString(0),
                    AircraftTail = reader.GetString(1),
                    OriginCode = reader.GetString(2),
                    DestinationCode = reader.GetString(3),
                    PlannedSpeedMs = reader.GetDouble(4),
                    StartTimeUtc = reader.GetDateTime(5).ToUniversalTime(),
                    Status = reader.GetString(6),
                    Progress = reader.GetDouble(7)
                });
            }

            return plans;
        }

        public async Task<FlightPlanSummary?> GetFlightPlanAsync(string callsign, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(callsign))
            {
                return null;
            }

            const string sql = @"
SELECT fp.callsign, fp.aircraft_tail, fp.origin_code, fp.destination_code,
       fp.planned_speed_ms, fs.start_time, fs.status, fs.progress
FROM flight_plans fp
JOIN flight_states fs ON fp.callsign = fs.callsign
WHERE fp.callsign = @callsign;";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@callsign", callsign);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new FlightPlanSummary
                {
                    Callsign = reader.GetString(0),
                    AircraftTail = reader.GetString(1),
                    OriginCode = reader.GetString(2),
                    DestinationCode = reader.GetString(3),
                    PlannedSpeedMs = reader.GetDouble(4),
                    StartTimeUtc = reader.GetDateTime(5).ToUniversalTime(),
                    Status = reader.GetString(6),
                    Progress = reader.GetDouble(7)
                };
            }

            return null;
        }

        public async Task<FlightPlanSummary?> CreateFlightPlanAsync(FlightPlanRequest request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var callsign = request.Callsign.Trim().ToUpperInvariant();
            var aircraftTail = request.AircraftTail.Trim().ToUpperInvariant();
            var originCode = request.OriginCode.Trim().ToUpperInvariant();
            var destinationCode = request.DestinationCode.Trim().ToUpperInvariant();

            if (originCode == destinationCode)
            {
                throw new InvalidOperationException("Origin and destination cannot be the same.");
            }

            await EnsureCallsignIsUnique(connection, transaction, callsign, cancellationToken);

            var origin = await LoadAirportAsync(connection, transaction, originCode, cancellationToken)
                         ?? throw new InvalidOperationException($"Origin airport '{originCode}' not found.");
            var destination = await LoadAirportAsync(connection, transaction, destinationCode, cancellationToken)
                                ?? throw new InvalidOperationException($"Destination airport '{destinationCode}' not found.");

            var aircraft = await LoadAircraftAsync(connection, transaction, aircraftTail, cancellationToken)
                           ?? throw new InvalidOperationException($"Aircraft '{aircraftTail}' not found.");

            await EnsureAircraftAvailableAsync(connection, transaction, aircraftTail, cancellationToken);

            var now = DateTime.UtcNow;
            var desiredStart = request.StartTimeUtc.Kind switch
            {
                DateTimeKind.Utc => request.StartTimeUtc,
                DateTimeKind.Local => request.StartTimeUtc.ToUniversalTime(),
                _ => DateTime.SpecifyKind(request.StartTimeUtc, DateTimeKind.Utc)
            };

            if (desiredStart < now)
            {
                desiredStart = now.AddSeconds(5);
            }

            var plannedSpeed = Math.Clamp(request.PlannedSpeedMs ?? aircraft.CruiseSpeedMs, 10, 400);
            var offsetSeconds = (int)Math.Max(0, Math.Round((desiredStart - now).TotalSeconds));
            var heading = Math.Atan2(destination.Longitude - origin.Longitude, destination.Latitude - origin.Latitude);

            await using (var insertPlan = new NpgsqlCommand(@"
                INSERT INTO flight_plans (callsign, aircraft_tail, origin_code, destination_code, planned_speed_ms, start_offset_seconds)
                VALUES (@callsign, @tail, @origin, @dest, @speed, @offset);
            ", connection, transaction))
            {
                insertPlan.Parameters.AddWithValue("@callsign", callsign);
                insertPlan.Parameters.AddWithValue("@tail", aircraftTail);
                insertPlan.Parameters.AddWithValue("@origin", originCode);
                insertPlan.Parameters.AddWithValue("@dest", destinationCode);
                insertPlan.Parameters.AddWithValue("@speed", plannedSpeed);
                insertPlan.Parameters.AddWithValue("@offset", offsetSeconds);
                await insertPlan.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var insertState = new NpgsqlCommand(@"
                INSERT INTO flight_states (callsign, status, current_lat, current_lon, altitude, heading, speed_ms, progress, start_time)
                VALUES (@callsign, @status, @lat, @lon, 0, @heading, @speed, 0, @startTime)
                ON CONFLICT (callsign) DO UPDATE SET
                    status = EXCLUDED.status,
                    current_lat = EXCLUDED.current_lat,
                    current_lon = EXCLUDED.current_lon,
                    altitude = EXCLUDED.altitude,
                    heading = EXCLUDED.heading,
                    speed_ms = EXCLUDED.speed_ms,
                    progress = EXCLUDED.progress,
                    start_time = EXCLUDED.start_time,
                    updated_at = NOW();
            ", connection, transaction))
            {
                insertState.Parameters.AddWithValue("@callsign", callsign);
                insertState.Parameters.AddWithValue("@status", "WAITING");
                insertState.Parameters.AddWithValue("@lat", origin.Latitude);
                insertState.Parameters.AddWithValue("@lon", origin.Longitude);
                insertState.Parameters.AddWithValue("@heading", heading);
                insertState.Parameters.AddWithValue("@speed", plannedSpeed);
                insertState.Parameters.AddWithValue("@startTime", desiredStart);
                await insertState.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return await GetFlightPlanAsync(callsign, cancellationToken);
        }

        public async Task BulkUpsertAsync(IEnumerable<Flight> flights, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await UpdateStatesAsync(connection, flights, cancellationToken);
        }

        public async Task ResetFlightsAsync(IEnumerable<Flight> flights, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await UpdateStatesAsync(connection, flights, cancellationToken);
        }

        private static Flight ReadFlight(NpgsqlDataReader reader)
        {
            return new Flight
            {
                Callsign = reader.GetString(0),
                From = reader.GetString(1),
                To = reader.GetString(2),
                OriginName = reader.GetString(3),
                OriginLat = reader.GetDouble(4),
                OriginLon = reader.GetDouble(5),
                DestinationName = reader.GetString(6),
                DestLat = reader.GetDouble(7),
                DestLon = reader.GetDouble(8),
                StartOffsetSeconds = reader.GetInt32(10),
                StartTime = reader.GetDateTime(11).ToUniversalTime(),
                Status = reader.GetString(12),
                CurrentLat = reader.GetDouble(13),
                CurrentLon = reader.GetDouble(14),
                Altitude = reader.GetDouble(15),
                Heading = reader.GetDouble(16),
                SpeedMs = reader.GetDouble(17),
                Progress = reader.GetDouble(18),
                AircraftTail = reader.GetString(19),
                AircraftModel = reader.GetString(20),
                AircraftManufacturer = reader.IsDBNull(21) ? string.Empty : reader.GetString(21)
            };
        }

        private async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            var commands = new[]
            {
                @"CREATE TABLE IF NOT EXISTS airports (
                    code TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    city TEXT NULL,
                    country TEXT NULL,
                    latitude DOUBLE PRECISION NOT NULL,
                    longitude DOUBLE PRECISION NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS aircraft (
                    tail_number TEXT PRIMARY KEY,
                    model TEXT NOT NULL,
                    manufacturer TEXT NULL,
                    cruise_speed_ms DOUBLE PRECISION NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS flight_plans (
                    callsign TEXT PRIMARY KEY,
                    aircraft_tail TEXT NOT NULL REFERENCES aircraft(tail_number),
                    origin_code TEXT NOT NULL REFERENCES airports(code),
                    destination_code TEXT NOT NULL REFERENCES airports(code),
                    planned_speed_ms DOUBLE PRECISION NOT NULL,
                    start_offset_seconds INTEGER NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );",
                @"CREATE TABLE IF NOT EXISTS flight_states (
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
                );"
            };

            foreach (var sql in commands)
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task<Dictionary<string, AirportSeedDto>> EnsureAirportsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            var existing = await FetchAirportsAsync(connection, cancellationToken);
            if (existing.Count > 0)
            {
                return existing;
            }

            var seeds = await LoadSeedAsync<AirportSeedDto>("airports.json", cancellationToken);
            foreach (var airport in seeds)
            {
                await using var command = new NpgsqlCommand(@"
                    INSERT INTO airports (code, name, city, country, latitude, longitude)
                    VALUES (@code, @name, @city, @country, @lat, @lon)
                    ON CONFLICT (code) DO NOTHING;", connection);
                command.Parameters.AddWithValue("@code", airport.Code);
                command.Parameters.AddWithValue("@name", airport.Name);
                command.Parameters.AddWithValue("@city", (object?)airport.City ?? DBNull.Value);
                command.Parameters.AddWithValue("@country", (object?)airport.Country ?? DBNull.Value);
                command.Parameters.AddWithValue("@lat", airport.Latitude);
                command.Parameters.AddWithValue("@lon", airport.Longitude);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("Seeded {Count} airports.", seeds.Count);
            return seeds.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<string, AirportSeedDto>> FetchAirportsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, AirportSeedDto>(StringComparer.OrdinalIgnoreCase);
            await using var command = new NpgsqlCommand("SELECT code, name, city, country, latitude, longitude FROM airports;", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetString(0)] = new AirportSeedDto
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    City = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Country = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Latitude = reader.GetDouble(4),
                    Longitude = reader.GetDouble(5)
                };
            }

            return result;
        }

        private async Task<Dictionary<string, AircraftSeedDto>> EnsureAircraftAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            var existing = await FetchAircraftAsync(connection, cancellationToken);
            if (existing.Count > 0)
            {
                return existing;
            }

            var seeds = await LoadSeedAsync<AircraftSeedDto>("aircraft.json", cancellationToken);
            foreach (var aircraft in seeds)
            {
                await using var command = new NpgsqlCommand(@"
                    INSERT INTO aircraft (tail_number, model, manufacturer, cruise_speed_ms)
                    VALUES (@tail, @model, @manufacturer, @speed)
                    ON CONFLICT (tail_number) DO NOTHING;", connection);
                command.Parameters.AddWithValue("@tail", aircraft.TailNumber);
                command.Parameters.AddWithValue("@model", aircraft.Model);
                command.Parameters.AddWithValue("@manufacturer", (object?)aircraft.Manufacturer ?? DBNull.Value);
                command.Parameters.AddWithValue("@speed", aircraft.CruiseSpeedMs);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("Seeded {Count} aircraft.", seeds.Count);
            return seeds.ToDictionary(a => a.TailNumber, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<string, AircraftSeedDto>> FetchAircraftAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, AircraftSeedDto>(StringComparer.OrdinalIgnoreCase);
            await using var command = new NpgsqlCommand("SELECT tail_number, model, manufacturer, cruise_speed_ms FROM aircraft;", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetString(0)] = new AircraftSeedDto
                {
                    TailNumber = reader.GetString(0),
                    Model = reader.GetString(1),
                    Manufacturer = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CruiseSpeedMs = reader.GetDouble(3)
                };
            }

            return result;
        }

        private async Task EnsureFlightPlansAsync(NpgsqlConnection connection, Dictionary<string, AirportSeedDto> airports, Dictionary<string, AircraftSeedDto> aircraft, CancellationToken cancellationToken)
        {
            await using var countPlansCmd = new NpgsqlCommand("SELECT COUNT(*) FROM flight_plans;", connection);
            var plansCount = (long)(await countPlansCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);

            if (plansCount == 0)
            {
                var seeds = await LoadSeedAsync<FlightSeedDto>("flights.json", cancellationToken);
                var now = DateTime.UtcNow;

                foreach (var seed in seeds)
                {
                    if (!airports.TryGetValue(seed.From, out var origin))
                    {
                        _logger.LogWarning("Skipping flight {Callsign} because origin airport {Origin} is missing.", seed.Callsign, seed.From);
                        continue;
                    }

                    if (!airports.TryGetValue(seed.To, out var destination))
                    {
                        _logger.LogWarning("Skipping flight {Callsign} because destination airport {Dest} is missing.", seed.Callsign, seed.To);
                        continue;
                    }

                    if (!aircraft.ContainsKey(seed.AircraftTail))
                    {
                        _logger.LogWarning("Skipping flight {Callsign} because aircraft {Tail} is missing.", seed.Callsign, seed.AircraftTail);
                        continue;
                    }

                    await using (var planCommand = new NpgsqlCommand(@"
                        INSERT INTO flight_plans (callsign, aircraft_tail, origin_code, destination_code, planned_speed_ms, start_offset_seconds)
                        VALUES (@callsign, @tail, @origin, @dest, @speed, @offset)
                        ON CONFLICT (callsign) DO NOTHING;", connection))
                    {
                        planCommand.Parameters.AddWithValue("@callsign", seed.Callsign);
                        planCommand.Parameters.AddWithValue("@tail", seed.AircraftTail);
                        planCommand.Parameters.AddWithValue("@origin", seed.From);
                        planCommand.Parameters.AddWithValue("@dest", seed.To);
                        planCommand.Parameters.AddWithValue("@speed", seed.SpeedMs);
                        planCommand.Parameters.AddWithValue("@offset", seed.StartTimeOffset);
                        await planCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var startTime = now.AddSeconds(seed.StartTimeOffset);
                    var heading = Math.Atan2(destination.Longitude - origin.Longitude, destination.Latitude - origin.Latitude);

                    await using (var stateCommand = new NpgsqlCommand(@"
                        INSERT INTO flight_states (callsign, status, current_lat, current_lon, altitude, heading, speed_ms, progress, start_time)
                        VALUES (@callsign, @status, @lat, @lon, 0, @heading, @speed, 0, @startTime)
                        ON CONFLICT (callsign) DO NOTHING;", connection))
                    {
                        stateCommand.Parameters.AddWithValue("@callsign", seed.Callsign);
                        stateCommand.Parameters.AddWithValue("@status", "WAITING");
                        stateCommand.Parameters.AddWithValue("@lat", origin.Latitude);
                        stateCommand.Parameters.AddWithValue("@lon", origin.Longitude);
                        stateCommand.Parameters.AddWithValue("@heading", heading);
                        stateCommand.Parameters.AddWithValue("@speed", seed.SpeedMs);
                        stateCommand.Parameters.AddWithValue("@startTime", startTime);
                        await stateCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                _logger.LogInformation("Seeded {Count} flight plans and states.", seeds.Count);
            }

            await using var countStatesCmd = new NpgsqlCommand("SELECT COUNT(*) FROM flight_states;", connection);
            var stateCount = (long)(await countStatesCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (stateCount == 0)
            {
                await RebuildStatesAsync(connection, airports, cancellationToken);
            }
        }

        private async Task RebuildStatesAsync(NpgsqlConnection connection, Dictionary<string, AirportSeedDto> airports, CancellationToken cancellationToken)
        {
            var plans = new List<(string Callsign, string Origin, string Destination, double Speed, int Offset)>();

            await using (var command = new NpgsqlCommand("SELECT callsign, origin_code, destination_code, planned_speed_ms, start_offset_seconds FROM flight_plans;", connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    plans.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3), reader.GetInt32(4)));
                }
            }

            var now = DateTime.UtcNow;
            foreach (var plan in plans)
            {
                if (!airports.TryGetValue(plan.Origin, out var origin) || !airports.TryGetValue(plan.Destination, out var destination))
                {
                    continue;
                }

                var heading = Math.Atan2(destination.Longitude - origin.Longitude, destination.Latitude - origin.Latitude);

                await using var command = new NpgsqlCommand(@"
                    INSERT INTO flight_states (callsign, status, current_lat, current_lon, altitude, heading, speed_ms, progress, start_time)
                    VALUES (@callsign, @status, @lat, @lon, 0, @heading, @speed, 0, @startTime)
                    ON CONFLICT (callsign) DO UPDATE SET
                        status = EXCLUDED.status,
                        current_lat = EXCLUDED.current_lat,
                        current_lon = EXCLUDED.current_lon,
                        altitude = EXCLUDED.altitude,
                        heading = EXCLUDED.heading,
                        speed_ms = EXCLUDED.speed_ms,
                        progress = EXCLUDED.progress,
                        start_time = EXCLUDED.start_time,
                        updated_at = NOW();", connection);

                command.Parameters.AddWithValue("@callsign", plan.Callsign);
                command.Parameters.AddWithValue("@status", "WAITING");
                command.Parameters.AddWithValue("@lat", origin.Latitude);
                command.Parameters.AddWithValue("@lon", origin.Longitude);
                command.Parameters.AddWithValue("@heading", heading);
                command.Parameters.AddWithValue("@speed", plan.Speed);
                command.Parameters.AddWithValue("@startTime", now.AddSeconds(plan.Offset));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task UpdateStatesAsync(NpgsqlConnection connection, IEnumerable<Flight> flights, CancellationToken cancellationToken)
        {
            const string sql = @"
UPDATE flight_states
SET status = @status,
    current_lat = @lat,
    current_lon = @lon,
    altitude = @altitude,
    heading = @heading,
    speed_ms = @speed,
    progress = @progress,
    start_time = @startTime,
    updated_at = NOW()
WHERE callsign = @callsign;";

            foreach (var flight in flights)
            {
                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@callsign", flight.Callsign);
                command.Parameters.AddWithValue("@status", flight.Status);
                command.Parameters.AddWithValue("@lat", flight.CurrentLat);
                command.Parameters.AddWithValue("@lon", flight.CurrentLon);
                command.Parameters.AddWithValue("@altitude", flight.Altitude);
                command.Parameters.AddWithValue("@heading", flight.Heading);
                command.Parameters.AddWithValue("@speed", flight.SpeedMs);
                command.Parameters.AddWithValue("@progress", flight.Progress);
                command.Parameters.AddWithValue("@startTime", flight.StartTime.ToUniversalTime());

                var updated = await command.ExecuteNonQueryAsync(cancellationToken);
                if (updated == 0)
                {
                    _logger.LogWarning("Flight state missing for {Callsign}; rebuilding entry.", flight.Callsign);
                    await RebuildStatesAsync(connection, await FetchAirportsAsync(connection, cancellationToken), cancellationToken);
                    break;
                }
            }
        }

        private static async Task EnsureCallsignIsUnique(NpgsqlConnection connection, NpgsqlTransaction transaction, string callsign, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM flight_plans WHERE callsign = @callsign;", connection, transaction);
            command.Parameters.AddWithValue("@callsign", callsign);

            var existing = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (existing > 0)
            {
                throw new InvalidOperationException($"Flight plan with callsign '{callsign}' already exists.");
            }
        }

        private static async Task<AirportSeedDto?> LoadAirportAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string code, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("SELECT code, name, city, country, latitude, longitude FROM airports WHERE code = @code;", connection, transaction);
            command.Parameters.AddWithValue("@code", code);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var airport = new AirportSeedDto
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    City = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Country = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Latitude = reader.GetDouble(4),
                    Longitude = reader.GetDouble(5)
                };

                return airport;
            }

            return null;
        }

        private static async Task<AircraftSeedDto?> LoadAircraftAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tailNumber, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("SELECT tail_number, model, manufacturer, cruise_speed_ms FROM aircraft WHERE tail_number = @tail;", connection, transaction);
            command.Parameters.AddWithValue("@tail", tailNumber);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new AircraftSeedDto
                {
                    TailNumber = reader.GetString(0),
                    Model = reader.GetString(1),
                    Manufacturer = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CruiseSpeedMs = reader.GetDouble(3)
                };
            }

            return null;
        }

        private static async Task EnsureAircraftAvailableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tailNumber, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(@"
                SELECT COUNT(*)
                FROM flight_plans fp
                JOIN flight_states fs ON fp.callsign = fs.callsign
                WHERE fp.aircraft_tail = @tail
                  AND fs.status IN ('ACTIVE', 'WAITING');
            ", connection, transaction);

            command.Parameters.AddWithValue("@tail", tailNumber);

            var inUse = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (inUse > 0)
            {
                throw new InvalidOperationException($"Aircraft '{tailNumber}' is already assigned to an active flight.");
            }
        }

        private async Task<List<T>> LoadSeedAsync<T>(string fileName, CancellationToken cancellationToken)
        {
            var datasetPath = Path.Combine(_environment.ContentRootPath, "Data", fileName);
            if (!File.Exists(datasetPath))
            {
                _logger.LogWarning("Seed dataset not found at {Path}", datasetPath);
                return new List<T>();
            }

            await using var stream = File.OpenRead(datasetPath);
            var items = await JsonSerializer.DeserializeAsync<List<T>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, cancellationToken);

            return items ?? new List<T>();
        }

        private sealed class AirportSeedDto
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? City { get; set; }
            public string? Country { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }

        private sealed class AircraftSeedDto
        {
            public string TailNumber { get; set; } = string.Empty;
            public string Model { get; set; } = string.Empty;
            public string? Manufacturer { get; set; }
            public double CruiseSpeedMs { get; set; }
        }

        private sealed class FlightSeedDto
        {
            public string Callsign { get; set; } = string.Empty;
            public string From { get; set; } = string.Empty;
            public string To { get; set; } = string.Empty;
            public string AircraftTail { get; set; } = string.Empty;
            public double SpeedMs { get; set; }
            public int StartTimeOffset { get; set; }
        }
    }
}
