namespace FlightRadarAPI.Models
{
    /// <summary>
    /// Represents a single telemetry sample captured during flight.
    /// Stored at regular intervals (typically 1 second) for post-flight analysis.
    /// </summary>
    public class FlightTelemetrySample
    {
        /// <summary>Callsign of the flight this sample belongs to</summary>
        public string Callsign { get; set; } = string.Empty;
        
        /// <summary>UTC timestamp when this sample was captured</summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>Seconds since flight start</summary>
        public double ElapsedSeconds { get; set; }
        
        // Position
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Altitude { get; set; }           // meters
        public double AltitudeFeet { get; set; }       // feet
        
        // Attitude
        public double Heading { get; set; }            // radians
        public double HeadingDegrees { get; set; }     // degrees
        public double Pitch { get; set; }              // radians
        public double PitchDegrees { get; set; }       // degrees
        public double Roll { get; set; }               // radians
        public double RollDegrees { get; set; }        // degrees
        public double AngleOfAttack { get; set; }      // radians
        
        // Speeds
        public double TrueAirspeed { get; set; }       // m/s
        public double IndicatedAirspeed { get; set; }  // m/s
        public double GroundSpeed { get; set; }        // m/s
        public double VerticalSpeed { get; set; }      // m/s (positive = climb)
        public double VerticalSpeedFpm { get; set; }   // ft/min
        public double Mach { get; set; }
        public double SpeedKnots { get; set; }         // TAS in knots
        
        // Forces & Aerodynamics
        public double Lift { get; set; }               // Newtons
        public double Drag { get; set; }               // Newtons
        public double Thrust { get; set; }             // Newtons
        public double LiftToDrag { get; set; }
        
        // Engine & Fuel
        public double Throttle { get; set; }           // 0-1
        public double FuelRemaining { get; set; }      // kg
        public double FuelFlowRate { get; set; }       // kg/s
        public double GrossWeight { get; set; }        // kg
        
        // Flight Phase
        public FlightPhase Phase { get; set; }
        public string Status { get; set; } = string.Empty;
        
        // Progress
        public double Progress { get; set; }           // 0-1
        public double DistanceFlown { get; set; }      // meters
        public double DistanceRemaining { get; set; }  // meters
        
        /// <summary>
        /// Creates a telemetry sample from the current flight state.
        /// </summary>
        public static FlightTelemetrySample FromFlight(Flight flight, DateTime timestamp)
        {
            return new FlightTelemetrySample
            {
                Callsign = flight.Callsign,
                Timestamp = timestamp,
                ElapsedSeconds = flight.FlightTimeSeconds,
                
                Latitude = flight.CurrentLat,
                Longitude = flight.CurrentLon,
                Altitude = flight.Altitude,
                AltitudeFeet = flight.AltitudeFeet,
                
                Heading = flight.Heading,
                HeadingDegrees = flight.HeadingDegrees,
                Pitch = flight.Pitch,
                PitchDegrees = flight.PitchDegrees,
                Roll = flight.Roll,
                RollDegrees = flight.RollDegrees,
                AngleOfAttack = flight.AngleOfAttack,
                
                TrueAirspeed = flight.TrueAirspeed,
                IndicatedAirspeed = flight.IndicatedAirspeed,
                GroundSpeed = flight.GroundSpeed,
                VerticalSpeed = flight.VerticalSpeed,
                VerticalSpeedFpm = flight.VerticalSpeedFpm,
                Mach = flight.Mach,
                SpeedKnots = flight.SpeedKnots,
                
                Lift = flight.Lift,
                Drag = flight.Drag,
                Thrust = flight.Thrust,
                LiftToDrag = flight.LiftToDragRatio,
                
                Throttle = flight.Throttle,
                FuelRemaining = flight.FuelRemaining,
                FuelFlowRate = flight.FuelFlowRate,
                GrossWeight = flight.GrossWeight,
                
                Phase = flight.Phase,
                Status = flight.Status,
                
                Progress = flight.Progress,
                DistanceFlown = flight.DistanceFlown,
                DistanceRemaining = flight.DistanceRemaining
            };
        }
    }
}
