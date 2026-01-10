using System.Text.Json;

namespace FlightRadarAPI.Models
{
    /// <summary>
    /// Comprehensive flight report generated after a flight is completed.
    /// Contains summary statistics, phase breakdown, and telemetry data.
    /// </summary>
    public class FlightReport
    {
        // ===== Flight Identification =====
        public string Callsign { get; set; } = string.Empty;
        public string AircraftTail { get; set; } = string.Empty;
        public string AircraftModel { get; set; } = string.Empty;
        public string AircraftManufacturer { get; set; } = string.Empty;
        
        // ===== Route Information =====
        public string OriginCode { get; set; } = string.Empty;
        public string OriginName { get; set; } = string.Empty;
        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        
        public string DestinationCode { get; set; } = string.Empty;
        public string DestinationName { get; set; } = string.Empty;
        public double DestinationLat { get; set; }
        public double DestinationLon { get; set; }
        
        public double GreatCircleDistance { get; set; }        // meters
        public double GreatCircleDistanceNm { get; set; }      // nautical miles
        
        // ===== Timing =====
        public DateTime DepartureTimeUtc { get; set; }
        public DateTime ArrivalTimeUtc { get; set; }
        public TimeSpan FlightDuration { get; set; }
        public string FlightDurationFormatted { get; set; } = string.Empty;
        
        // ===== Distance Statistics =====
        public double ActualDistanceFlown { get; set; }        // meters
        public double ActualDistanceFlownNm { get; set; }      // nautical miles
        public double RouteEfficiency { get; set; }            // actual/great-circle (ideally close to 1.0)
        
        // ===== Altitude Statistics =====
        public double MaxAltitude { get; set; }                // meters
        public double MaxAltitudeFeet { get; set; }            // feet
        public double AverageAltitude { get; set; }            // meters
        public double CruiseAltitude { get; set; }             // meters (most common cruising altitude)
        
        // ===== Speed Statistics =====
        public double MaxSpeed { get; set; }                   // m/s TAS
        public double MaxSpeedKnots { get; set; }
        public double MaxMach { get; set; }
        public double AverageSpeed { get; set; }               // m/s TAS
        public double AverageSpeedKnots { get; set; }
        public double AverageMach { get; set; }
        
        // ===== Vertical Performance =====
        public double MaxClimbRate { get; set; }               // m/s
        public double MaxClimbRateFpm { get; set; }            // ft/min
        public double MaxDescentRate { get; set; }             // m/s
        public double MaxDescentRateFpm { get; set; }          // ft/min
        public double AverageClimbRate { get; set; }           // m/s (during climb phase)
        public double AverageDescentRate { get; set; }         // m/s (during descent phase)
        
        // ===== Fuel Statistics =====
        public double InitialFuel { get; set; }                // kg
        public double FinalFuel { get; set; }                  // kg
        public double TotalFuelConsumed { get; set; }          // kg
        public double AverageFuelFlow { get; set; }            // kg/s
        public double FuelEfficiency { get; set; }             // kg per 100nm
        
        // ===== Aerodynamic Statistics =====
        public double MaxLiftToDrag { get; set; }
        public double AverageLiftToDrag { get; set; }
        public double MaxAngleOfAttack { get; set; }           // degrees
        public double MaxBankAngle { get; set; }               // degrees
        
        // ===== Flight Phase Breakdown =====
        public List<FlightPhaseRecord> PhaseBreakdown { get; set; } = new();
        
        // ===== Telemetry Data =====
        public int TotalSamples { get; set; }
        public double SampleIntervalSeconds { get; set; }
        
        /// <summary>
        /// Full telemetry samples (can be large - use sparingly or stream to file)
        /// </summary>
        public List<FlightTelemetrySample>? TelemetryData { get; set; }
        
        // ===== Report Metadata =====
        public DateTime ReportGeneratedUtc { get; set; }
        public string ReportVersion { get; set; } = "1.0";
        
        /// <summary>
        /// Generates a comprehensive report from a completed flight and its telemetry.
        /// </summary>
        public static FlightReport Generate(Flight flight, List<FlightTelemetrySample> telemetry)
        {
            var report = new FlightReport
            {
                Callsign = flight.Callsign,
                AircraftTail = flight.AircraftTail,
                AircraftModel = flight.AircraftModel,
                AircraftManufacturer = flight.AircraftManufacturer,
                
                OriginCode = flight.From,
                OriginName = flight.OriginName,
                OriginLat = flight.OriginLat,
                OriginLon = flight.OriginLon,
                
                DestinationCode = flight.To,
                DestinationName = flight.DestinationName,
                DestinationLat = flight.DestLat,
                DestinationLon = flight.DestLon,
                
                GreatCircleDistance = flight.TotalRouteDistance,
                GreatCircleDistanceNm = flight.TotalRouteDistance / 1852.0,
                
                DepartureTimeUtc = flight.StartTime,
                ArrivalTimeUtc = flight.LandingTime ?? DateTime.UtcNow,
                FlightDuration = TimeSpan.FromSeconds(flight.FlightTimeSeconds),
                FlightDurationFormatted = TimeSpan.FromSeconds(flight.FlightTimeSeconds).ToString(@"hh\:mm\:ss"),
                
                ActualDistanceFlown = flight.DistanceFlown,
                ActualDistanceFlownNm = flight.DistanceFlown / 1852.0,
                RouteEfficiency = flight.TotalRouteDistance > 0 ? flight.TotalRouteDistance / flight.DistanceFlown : 1.0,
                
                MaxAltitude = flight.MaxAltitude,
                MaxAltitudeFeet = flight.MaxAltitude * 3.28084,
                AverageAltitude = flight.AverageAltitude,
                
                MaxSpeed = flight.MaxSpeed,
                MaxSpeedKnots = flight.MaxSpeed * 1.94384,
                MaxMach = flight.MaxMach,
                AverageSpeed = flight.AverageSpeed,
                AverageSpeedKnots = flight.AverageSpeed * 1.94384,
                
                MaxClimbRate = flight.MaxVerticalSpeed,
                MaxClimbRateFpm = flight.MaxVerticalSpeed * 196.85,
                
                TotalFuelConsumed = flight.FuelConsumed,
                AverageFuelFlow = flight.AverageFuelFlow,
                
                TotalSamples = telemetry.Count,
                SampleIntervalSeconds = telemetry.Count > 1 ? 
                    (telemetry[^1].ElapsedSeconds - telemetry[0].ElapsedSeconds) / (telemetry.Count - 1) : 1.0,
                
                TelemetryData = telemetry,
                ReportGeneratedUtc = DateTime.UtcNow
            };
            
            // Calculate phase breakdown from telemetry
            report.PhaseBreakdown = CalculatePhaseBreakdown(telemetry);
            
            // Calculate additional statistics from telemetry
            if (telemetry.Count > 0)
            {
                report.InitialFuel = telemetry[0].FuelRemaining + telemetry.Sum(t => t.FuelFlowRate);
                report.FinalFuel = telemetry[^1].FuelRemaining;
                report.CruiseAltitude = GetCruiseAltitude(telemetry);
                report.AverageMach = telemetry.Average(t => t.Mach);
                report.MaxLiftToDrag = telemetry.Max(t => t.LiftToDrag);
                report.AverageLiftToDrag = telemetry.Average(t => t.LiftToDrag);
                report.MaxAngleOfAttack = telemetry.Max(t => t.AngleOfAttack) * 180 / Math.PI;
                report.MaxBankAngle = telemetry.Max(t => Math.Abs(t.Roll)) * 180 / Math.PI;
                report.MaxDescentRate = telemetry.Where(t => t.VerticalSpeed < 0).Select(t => Math.Abs(t.VerticalSpeed)).DefaultIfEmpty(0).Max();
                report.MaxDescentRateFpm = report.MaxDescentRate * 196.85;
                
                var climbSamples = telemetry.Where(t => t.Phase == FlightPhase.Climb && t.VerticalSpeed > 0).ToList();
                report.AverageClimbRate = climbSamples.Count > 0 ? climbSamples.Average(t => t.VerticalSpeed) : 0;
                
                var descentSamples = telemetry.Where(t => t.Phase == FlightPhase.Descent && t.VerticalSpeed < 0).ToList();
                report.AverageDescentRate = descentSamples.Count > 0 ? descentSamples.Average(t => Math.Abs(t.VerticalSpeed)) : 0;
                
                // Fuel efficiency: kg per 100nm
                if (report.ActualDistanceFlownNm > 0)
                {
                    report.FuelEfficiency = (report.TotalFuelConsumed / report.ActualDistanceFlownNm) * 100;
                }
            }
            
            return report;
        }
        
        private static List<FlightPhaseRecord> CalculatePhaseBreakdown(List<FlightTelemetrySample> telemetry)
        {
            var phases = new List<FlightPhaseRecord>();
            if (telemetry.Count == 0) return phases;
            
            FlightPhase currentPhase = telemetry[0].Phase;
            double phaseStartTime = telemetry[0].ElapsedSeconds;
            double phaseStartDistance = telemetry[0].DistanceFlown;
            double phaseStartFuel = telemetry[0].FuelRemaining;
            
            foreach (var sample in telemetry)
            {
                if (sample.Phase != currentPhase)
                {
                    // Record completed phase
                    phases.Add(new FlightPhaseRecord
                    {
                        Phase = currentPhase,
                        DurationSeconds = sample.ElapsedSeconds - phaseStartTime,
                        DistanceCovered = sample.DistanceFlown - phaseStartDistance,
                        FuelConsumed = phaseStartFuel - sample.FuelRemaining,
                        StartAltitude = telemetry.FirstOrDefault(t => t.ElapsedSeconds >= phaseStartTime)?.Altitude ?? 0,
                        EndAltitude = sample.Altitude
                    });
                    
                    // Start new phase
                    currentPhase = sample.Phase;
                    phaseStartTime = sample.ElapsedSeconds;
                    phaseStartDistance = sample.DistanceFlown;
                    phaseStartFuel = sample.FuelRemaining;
                }
            }
            
            // Record final phase
            var lastSample = telemetry[^1];
            phases.Add(new FlightPhaseRecord
            {
                Phase = currentPhase,
                DurationSeconds = lastSample.ElapsedSeconds - phaseStartTime,
                DistanceCovered = lastSample.DistanceFlown - phaseStartDistance,
                FuelConsumed = phaseStartFuel - lastSample.FuelRemaining,
                StartAltitude = telemetry.FirstOrDefault(t => t.ElapsedSeconds >= phaseStartTime)?.Altitude ?? 0,
                EndAltitude = lastSample.Altitude
            });
            
            return phases;
        }
        
        private static double GetCruiseAltitude(List<FlightTelemetrySample> telemetry)
        {
            var cruiseSamples = telemetry.Where(t => t.Phase == FlightPhase.Cruise).ToList();
            if (cruiseSamples.Count == 0) return 0;
            return cruiseSamples.Average(t => t.Altitude);
        }
        
        /// <summary>
        /// Exports the report to JSON format.
        /// </summary>
        public string ToJson(bool includeTelemetry = false)
        {
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            if (!includeTelemetry)
            {
                // Save and clear telemetry for smaller export
                var savedTelemetry = TelemetryData;
                TelemetryData = null;
                var json = JsonSerializer.Serialize(this, options);
                TelemetryData = savedTelemetry;
                return json;
            }
            
            return JsonSerializer.Serialize(this, options);
        }
        
        /// <summary>
        /// Exports telemetry data to CSV format.
        /// </summary>
        public string TelemetryToCsv()
        {
            if (TelemetryData == null || TelemetryData.Count == 0)
                return string.Empty;
            
            var sb = new System.Text.StringBuilder();
            
            // Header
            sb.AppendLine("Timestamp,ElapsedSeconds,Latitude,Longitude,Altitude_m,Altitude_ft," +
                         "Heading_deg,Pitch_deg,Roll_deg,AoA_deg," +
                         "TAS_ms,IAS_ms,GS_ms,VS_fpm,Mach,Speed_kts," +
                         "Lift_N,Drag_N,Thrust_N,L_D," +
                         "Throttle,Fuel_kg,FuelFlow_kgs,Weight_kg," +
                         "Phase,Progress,DistFlown_m,DistRemain_m");
            
            foreach (var s in TelemetryData)
            {
                sb.AppendLine($"{s.Timestamp:O},{s.ElapsedSeconds:F1}," +
                             $"{s.Latitude:F6},{s.Longitude:F6},{s.Altitude:F1},{s.AltitudeFeet:F0}," +
                             $"{s.HeadingDegrees:F1},{s.PitchDegrees:F2},{s.RollDegrees:F2},{s.AngleOfAttack * 180 / Math.PI:F2}," +
                             $"{s.TrueAirspeed:F1},{s.IndicatedAirspeed:F1},{s.GroundSpeed:F1},{s.VerticalSpeedFpm:F0},{s.Mach:F3},{s.SpeedKnots:F1}," +
                             $"{s.Lift:F0},{s.Drag:F0},{s.Thrust:F0},{s.LiftToDrag:F2}," +
                             $"{s.Throttle:F2},{s.FuelRemaining:F0},{s.FuelFlowRate:F3},{s.GrossWeight:F0}," +
                             $"{s.Phase},{s.Progress:F4},{s.DistanceFlown:F0},{s.DistanceRemaining:F0}");
            }
            
            return sb.ToString();
        }
    }
    
    /// <summary>
    /// Records statistics for a single flight phase.
    /// </summary>
    public record FlightPhaseRecord
    {
        public FlightPhase Phase { get; init; }
        public double DurationSeconds { get; init; }
        public string DurationFormatted => TimeSpan.FromSeconds(DurationSeconds).ToString(@"mm\:ss");
        public double DistanceCovered { get; init; }         // meters
        public double DistanceCoveredNm => DistanceCovered / 1852.0;
        public double FuelConsumed { get; init; }            // kg
        public double StartAltitude { get; init; }           // meters
        public double EndAltitude { get; init; }             // meters
    }
}
