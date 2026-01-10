using FlightRadarAPI.Models;
using FlightRadarAPI.Physics;

namespace FlightRadarAPI.Services
{
    /// <summary>
    /// Realistic flight physics engine that simulates aircraft behavior
    /// based on aerodynamics, atmosphere, and autopilot control.
    /// </summary>
    public class FlightPhysicsEngine
    {
        private readonly Dictionary<string, AircraftPerformance> _aircraftProfiles;

        public FlightPhysicsEngine()
        {
            _aircraftProfiles = AircraftPerformance.GetPresets();
        }

        /// <summary>
        /// Advances the flight state by the given time delta using realistic physics.
        /// </summary>
        public void UpdateFlight(Flight flight, double deltaSeconds, double speedMultiplier = 1.0)
        {
            double dt = deltaSeconds * speedMultiplier;
            if (dt <= 0) return;

            var aircraft = GetAircraftProfile(flight);
            
            // Update flight timing
            if (flight.Phase != FlightPhase.Preflight && flight.Phase != FlightPhase.Arrived)
            {
                flight.FlightTimeSeconds += dt;
                flight.TimeInCurrentPhase += dt;
            }

            // Execute phase-specific logic
            switch (flight.Phase)
            {
                case FlightPhase.Preflight:
                    UpdatePreflightPhase(flight, aircraft, dt);
                    break;
                case FlightPhase.Takeoff:
                    UpdateTakeoffPhase(flight, aircraft, dt);
                    break;
                case FlightPhase.Climb:
                    UpdateClimbPhase(flight, aircraft, dt);
                    break;
                case FlightPhase.Cruise:
                    UpdateCruisePhase(flight, aircraft, dt);
                    break;
                case FlightPhase.Descent:
                    UpdateDescentPhase(flight, aircraft, dt);
                    break;
                case FlightPhase.Approach:
                    UpdateApproachPhase(flight, aircraft, dt);
                    break;
                case FlightPhase.Landing:
                    UpdateLandingPhase(flight, aircraft, dt);
                    break;
            }

            // Calculate aerodynamic forces
            if (flight.TrueAirspeed > 10 && flight.Altitude > 0)
            {
                var forces = AerodynamicsCalculator.CalculateForces(
                    aircraft,
                    flight.TrueAirspeed,
                    flight.Altitude,
                    flight.GrossWeight,
                    flight.AngleOfAttack,
                    flight.Throttle,
                    flight.Phase == FlightPhase.Approach || flight.Phase == FlightPhase.Landing
                );

                flight.Lift = forces.Lift;
                flight.Drag = forces.Drag;
                flight.Thrust = forces.Thrust;
                flight.LiftToDragRatio = forces.LiftToDrag;
                flight.Mach = forces.Mach;
                flight.IndicatedAirspeed = forces.IndicatedAirspeed;
                flight.FuelFlowRate = forces.FuelFlowRate;
            }

            // Update fuel consumption
            if (flight.Throttle > 0 && flight.FuelRemaining > 0)
            {
                double fuelBurned = flight.FuelFlowRate * dt;
                flight.FuelRemaining = Math.Max(0, flight.FuelRemaining - fuelBurned);
                flight.FuelConsumed += fuelBurned;
                flight.GrossWeight = aircraft.EmptyWeight + flight.FuelRemaining;
            }

            // Update position along great-circle path
            UpdatePosition(flight, dt);

            // Update distances
            UpdateDistances(flight);

            // Update legacy compatibility fields
            flight.SpeedMs = flight.GroundSpeed;
            flight.TotalDistance = flight.DistanceFlown;

            // Update statistics
            UpdateStatistics(flight);

            // Update legacy status for frontend compatibility
            UpdateLegacyStatus(flight);
        }

        private AircraftPerformance GetAircraftProfile(Flight flight)
        {
            string key = $"{flight.AircraftManufacturer} {flight.AircraftModel}".Trim();
            if (string.IsNullOrEmpty(key))
            {
                key = flight.AircraftModel;
            }
            return AircraftPerformance.GetForModel(key);
        }

        private void UpdatePreflightPhase(Flight flight, AircraftPerformance aircraft, double dt)
        {
            // Initialize flight if not already done
            if (flight.GrossWeight <= 0)
            {
                // Assume 70% fuel load for initial calculation
                flight.FuelRemaining = aircraft.MaxFuel * 0.70;
                flight.GrossWeight = aircraft.EmptyWeight + flight.FuelRemaining;
            }

            // Calculate cruise altitude based on distance
            flight.TotalRouteDistance = CalculateGreatCircleDistance(
                flight.OriginLat, flight.OriginLon,
                flight.DestLat, flight.DestLon
            );

            // Set target altitude (higher for longer flights)
            double distanceNm = flight.TotalRouteDistance / 1852.0;
            if (distanceNm > 3000)
                flight.TargetAltitude = aircraft.CruiseAltitude;
            else if (distanceNm > 1500)
                flight.TargetAltitude = aircraft.CruiseAltitude * 0.95;
            else if (distanceNm > 500)
                flight.TargetAltitude = aircraft.CruiseAltitude * 0.85;
            else
                flight.TargetAltitude = 8000; // Short haul

            // Calculate target cruise speed
            var atm = AtmosphereModel.GetAtmosphereAt(flight.TargetAltitude);
            flight.TargetSpeed = aircraft.CruiseMach * atm.SpeedOfSound;

            // Transition to takeoff when start time is reached
            if (DateTime.UtcNow >= flight.StartTime)
            {
                TransitionToPhase(flight, FlightPhase.Takeoff);
            }
        }

        private void UpdateTakeoffPhase(Flight flight, AircraftPerformance aircraft, double dt)
        {
            // Accelerate on runway
            flight.Throttle = 1.0;
            
            // V1/Vr/V2 simulation
            double v2 = aircraft.VS0 * 1.2; // V2 is typically 1.2 * Vs
            double vRotate = v2 * 0.9;

            // Calculate thrust at sea level for takeoff
            var atm = AtmosphereModel.GetAtmosphereAt(flight.Altitude);
            double thrustLapse = Math.Pow(atm.DensityRatio, aircraft.ThrustLapseRate);
            flight.Thrust = aircraft.MaxThrustSeaLevel * thrustLapse * flight.Throttle;
            
            // Ground roll drag (simplified - mainly rolling friction)
            double rollingFriction = 0.02; // Coefficient for paved runway
            double weight = flight.GrossWeight * AtmosphereModel.GravitationalAcceleration;
            double rollingDrag = rollingFriction * weight;
            
            // Aerodynamic drag on ground roll
            double dynamicPressure = 0.5 * atm.Density * flight.GroundSpeed * flight.GroundSpeed;
            double aeroDrag = dynamicPressure * aircraft.WingArea * aircraft.Cd0;
            flight.Drag = rollingDrag + aeroDrag;

            if (flight.GroundSpeed < vRotate)
            {
                // Ground roll - accelerate
                double netForce = flight.Thrust - flight.Drag;
                double acceleration = netForce / flight.GrossWeight;
                acceleration = Math.Clamp(acceleration, 0, 4.0); // Limit to reasonable acceleration
                flight.GroundSpeed += acceleration * dt;
                flight.TrueAirspeed = flight.GroundSpeed;
                flight.Altitude = 0;
                flight.Pitch = 0;
                
                // Update fuel consumption during takeoff roll
                flight.FuelFlowRate = flight.Thrust * aircraft.TSFC;
            }
            else
            {
                // Rotate and climb
                flight.Pitch = Math.Min(flight.Pitch + 0.02 * dt, 0.15); // Max 8.6 degrees pitch
                flight.AngleOfAttack = flight.Pitch;
                
                // Initial climb at V2
                flight.TrueAirspeed = v2;
                flight.GroundSpeed = flight.TrueAirspeed;
                flight.VerticalSpeed = 10.0; // ~2000 fpm initial climb
                flight.Altitude += flight.VerticalSpeed * dt;
                
                // Update Mach during climb
                flight.Mach = flight.TrueAirspeed / atm.SpeedOfSound;
                flight.IndicatedAirspeed = AtmosphereModel.TasToIas(flight.TrueAirspeed, flight.Altitude);
            }

            // Transition to climb at 1500ft
            if (flight.Altitude > 450) // ~1500ft
            {
                TransitionToPhase(flight, FlightPhase.Climb);
            }
        }

        private void UpdateClimbPhase(Flight flight, AircraftPerformance aircraft, double dt)
        {
            // Calculate optimal climb speed
            double climbSpeed = AerodynamicsCalculator.CalculateBestCruiseSpeed(
                aircraft, flight.Altitude, flight.GrossWeight) * 0.9;
            
            // Accelerate towards climb speed
            double speedDiff = climbSpeed - flight.TrueAirspeed;
            double speedChange = Math.Sign(speedDiff) * Math.Min(Math.Abs(speedDiff), 2.0 * dt);
            flight.TrueAirspeed += speedChange;

            // Set throttle for climb
            flight.Throttle = 0.95;

            // Calculate achievable climb rate
            double maxClimb = AerodynamicsCalculator.CalculateMaxClimbRate(
                aircraft, flight.TrueAirspeed, flight.Altitude, flight.GrossWeight, flight.Throttle);
            
            // Limit climb rate based on altitude (reduced at high altitude)
            double altitudeFactor = 1.0 - (flight.Altitude / aircraft.ServiceCeiling) * 0.6;
            flight.VerticalSpeed = Math.Min(maxClimb, aircraft.MaxClimbRate) * altitudeFactor;
            
            // Update altitude
            flight.Altitude += flight.VerticalSpeed * dt;

            // Calculate pitch for climb
            flight.Pitch = Math.Asin(Math.Min(flight.VerticalSpeed / flight.TrueAirspeed, 0.15));
            flight.AngleOfAttack = flight.Pitch * 0.7;

            // Update ground speed
            flight.GroundSpeed = flight.TrueAirspeed * Math.Cos(flight.Pitch);

            // Navigate towards destination
            UpdateHeadingTowardsDestination(flight, dt);

            // Check if reached cruise altitude
            if (flight.Altitude >= flight.TargetAltitude * 0.98)
            {
                flight.Altitude = flight.TargetAltitude;
                TransitionToPhase(flight, FlightPhase.Cruise);
            }

            // Check if should start descent (Top of Descent calculation)
            double descentDistance = CalculateTopOfDescentDistance(flight, aircraft);
            if (flight.DistanceRemaining <= descentDistance)
            {
                TransitionToPhase(flight, FlightPhase.Descent);
            }
        }

        private void UpdateCruisePhase(Flight flight, AircraftPerformance aircraft, double dt)
        {
            // Maintain cruise altitude
            flight.VerticalSpeed = 0;
            flight.Altitude = flight.TargetAltitude;
            flight.Pitch = 0;

            // Accelerate/maintain cruise speed
            double targetTas = flight.TargetSpeed;
            double speedDiff = targetTas - flight.TrueAirspeed;
            flight.TrueAirspeed += Math.Sign(speedDiff) * Math.Min(Math.Abs(speedDiff), 1.0 * dt);

            // Adjust throttle for cruise
            double requiredThrust = flight.Drag;
            double maxThrust = aircraft.MaxThrustSeaLevel * 
                Math.Pow(AtmosphereModel.GetAtmosphereAt(flight.Altitude).DensityRatio, aircraft.ThrustLapseRate);
            flight.Throttle = Math.Clamp(requiredThrust / maxThrust, 0.3, 0.9);

            // Calculate AoA for level flight
            flight.AngleOfAttack = AerodynamicsCalculator.CalculateLevelFlightAoA(
                aircraft, flight.TrueAirspeed, flight.Altitude, flight.GrossWeight);

            // Update ground speed
            flight.GroundSpeed = flight.TrueAirspeed;

            // Navigate towards destination
            UpdateHeadingTowardsDestination(flight, dt);

            // Check for Top of Descent
            double descentDistance = CalculateTopOfDescentDistance(flight, aircraft);
            if (flight.DistanceRemaining <= descentDistance)
            {
                TransitionToPhase(flight, FlightPhase.Descent);
            }
        }

        private void UpdateDescentPhase(Flight flight, AircraftPerformance aircraft, double dt)
        {
            // Reduce throttle for descent
            flight.Throttle = 0.1;

            // Calculate descent profile (3 degree glide slope = ~5.2% gradient)
            double descentAngle = -0.052; // ~3 degrees
            double targetDescentRate = flight.TrueAirspeed * Math.Abs(descentAngle);
            
            // Limit descent rate
            flight.VerticalSpeed = -Math.Min(targetDescentRate, aircraft.MaxDescentRate);

            // Update altitude
            flight.Altitude += flight.VerticalSpeed * dt;
            flight.Altitude = Math.Max(0, flight.Altitude);

            // Maintain speed (reduce towards approach speed)
            double approachSpeed = aircraft.VS0 * 1.5;
            double speedReduction = (flight.TrueAirspeed - approachSpeed) * 0.01 * dt;
            flight.TrueAirspeed -= speedReduction;
            flight.TrueAirspeed = Math.Max(flight.TrueAirspeed, approachSpeed);

            // Calculate pitch for descent
            flight.Pitch = descentAngle;
            flight.AngleOfAttack = 0.05; // Small positive AoA during descent

            // Update ground speed
            flight.GroundSpeed = flight.TrueAirspeed * Math.Cos(Math.Abs(flight.Pitch));

            // Navigate towards destination
            UpdateHeadingTowardsDestination(flight, dt);

            // Transition to approach at 3000ft / 50nm
            if (flight.Altitude <= 900 || flight.DistanceRemaining <= 92600) // 50nm
            {
                TransitionToPhase(flight, FlightPhase.Approach);
            }
        }

        private void UpdateApproachPhase(Flight flight, AircraftPerformance aircraft, double dt)
        {
            // Configure for landing
            flight.Throttle = 0.25;

            // Slow to approach speed (Vref + 5)
            double vRef = aircraft.VS1 * 1.3;
            double targetSpeed = vRef + 5;

            double speedDiff = targetSpeed - flight.TrueAirspeed;
            flight.TrueAirspeed += Math.Sign(speedDiff) * Math.Min(Math.Abs(speedDiff), 2.0 * dt);

            // 3-degree glideslope
            double glideslope = -0.052;
            flight.VerticalSpeed = flight.TrueAirspeed * glideslope;
            flight.Altitude += flight.VerticalSpeed * dt;
            flight.Altitude = Math.Max(0, flight.Altitude);

            // Approach pitch
            flight.Pitch = glideslope;
            flight.AngleOfAttack = 0.08; // Higher AoA for slower speed

            flight.GroundSpeed = flight.TrueAirspeed * Math.Cos(Math.Abs(flight.Pitch));

            // Navigate towards destination
            UpdateHeadingTowardsDestination(flight, dt);

            // Transition to landing at 200ft
            if (flight.Altitude <= 60) // ~200ft
            {
                TransitionToPhase(flight, FlightPhase.Landing);
            }
        }

        private void UpdateLandingPhase(Flight flight, AircraftPerformance aircraft, double dt)
        {
            // Flare and touchdown
            flight.Throttle = 0;

            if (flight.Altitude > 0)
            {
                // Flare - reduce descent rate
                flight.VerticalSpeed = Math.Max(flight.VerticalSpeed, -2.0);
                flight.Pitch = 0.05; // Slight nose up for flare
                flight.Altitude += flight.VerticalSpeed * dt;
                flight.Altitude = Math.Max(0, flight.Altitude);
            }
            else
            {
                // On ground - decelerate
                flight.Altitude = 0;
                flight.VerticalSpeed = 0;
                flight.Pitch = 0;
                
                // Deceleration (brakes + thrust reversers)
                double deceleration = 3.0; // m/s²
                flight.GroundSpeed -= deceleration * dt;
                flight.TrueAirspeed = flight.GroundSpeed;

                if (flight.GroundSpeed <= 0)
                {
                    flight.GroundSpeed = 0;
                    flight.TrueAirspeed = 0;
                    flight.LandingTime = DateTime.UtcNow;
                    TransitionToPhase(flight, FlightPhase.Arrived);
                }
            }
        }

        private void TransitionToPhase(Flight flight, FlightPhase newPhase)
        {
            flight.Phase = newPhase;
            flight.TimeInCurrentPhase = 0;
        }

        private void UpdateHeadingTowardsDestination(Flight flight, double dt)
        {
            // Calculate bearing to destination
            double bearing = CalculateBearing(
                flight.CurrentLat, flight.CurrentLon,
                flight.DestLat, flight.DestLon);

            // Smoothly adjust heading (rate limited turn)
            double headingDiff = NormalizeAngle(bearing - flight.Heading);
            double maxTurnRate = 0.05; // ~3 degrees per second
            double turnAmount = Math.Sign(headingDiff) * Math.Min(Math.Abs(headingDiff), maxTurnRate * dt);
            
            flight.Heading = NormalizeAngle(flight.Heading + turnAmount);

            // Calculate bank angle for turn
            if (Math.Abs(turnAmount) > 0.001)
            {
                double turnRate = turnAmount / dt;
                flight.Roll = AerodynamicsCalculator.CalculateBankAngle(flight.TrueAirspeed, turnRate);
                flight.Roll = Math.Clamp(flight.Roll, -0.44, 0.44); // Max 25 degrees
            }
            else
            {
                flight.Roll = 0;
            }

            flight.TargetHeading = bearing;
        }

        private void UpdatePosition(Flight flight, double dt)
        {
            if (flight.GroundSpeed <= 0) return;

            // Distance traveled in this timestep
            double distance = flight.GroundSpeed * dt;

            // Update position along heading
            double lat1 = flight.CurrentLat * Math.PI / 180.0;
            double lon1 = flight.CurrentLon * Math.PI / 180.0;
            double heading = flight.Heading;

            // Earth radius
            const double R = 6371000.0;
            double angularDist = distance / R;

            double lat2 = Math.Asin(
                Math.Sin(lat1) * Math.Cos(angularDist) +
                Math.Cos(lat1) * Math.Sin(angularDist) * Math.Cos(heading));

            double lon2 = lon1 + Math.Atan2(
                Math.Sin(heading) * Math.Sin(angularDist) * Math.Cos(lat1),
                Math.Cos(angularDist) - Math.Sin(lat1) * Math.Sin(lat2));

            flight.CurrentLat = lat2 * 180.0 / Math.PI;
            flight.CurrentLon = lon2 * 180.0 / Math.PI;

            // Update distance flown
            flight.DistanceFlown += distance;
        }

        private void UpdateDistances(Flight flight)
        {
            flight.DistanceRemaining = CalculateGreatCircleDistance(
                flight.CurrentLat, flight.CurrentLon,
                flight.DestLat, flight.DestLon);

            // Update progress
            if (flight.TotalRouteDistance > 0)
            {
                flight.Progress = 1.0 - (flight.DistanceRemaining / flight.TotalRouteDistance);
                flight.Progress = Math.Clamp(flight.Progress, 0, 1);
            }
        }

        private void UpdateStatistics(Flight flight)
        {
            flight.TotalSamples++;
            
            flight.MaxAltitude = Math.Max(flight.MaxAltitude, flight.Altitude);
            flight.MaxSpeed = Math.Max(flight.MaxSpeed, flight.TrueAirspeed);
            flight.MaxMach = Math.Max(flight.MaxMach, flight.Mach);
            flight.MaxVerticalSpeed = Math.Max(flight.MaxVerticalSpeed, Math.Abs(flight.VerticalSpeed));

            // Running averages
            double n = flight.TotalSamples;
            flight.AverageSpeed = flight.AverageSpeed * ((n - 1) / n) + flight.TrueAirspeed / n;
            flight.AverageAltitude = flight.AverageAltitude * ((n - 1) / n) + flight.Altitude / n;
            flight.AverageFuelFlow = flight.AverageFuelFlow * ((n - 1) / n) + flight.FuelFlowRate / n;
        }

        private void UpdateLegacyStatus(Flight flight)
        {
            flight.Status = flight.Phase switch
            {
                FlightPhase.Preflight => "WAITING",
                FlightPhase.Arrived => "LANDED",
                _ => "ACTIVE"
            };
        }

        private double CalculateTopOfDescentDistance(Flight flight, AircraftPerformance aircraft)
        {
            // 3:1 rule - 3nm per 1000ft to lose
            double altitudeToLose = flight.Altitude;
            double distanceNm = (altitudeToLose / 304.8) * 3.0; // 304.8m = 1000ft
            return distanceNm * 1852.0; // Convert nm to meters
        }

        private static double CalculateGreatCircleDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000.0; // Earth radius in meters
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
        {
            double phi1 = lat1 * Math.PI / 180.0;
            double phi2 = lat2 * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;

            double y = Math.Sin(dLon) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) -
                       Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLon);

            return Math.Atan2(y, x);
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle < -Math.PI) angle += 2 * Math.PI;
            return angle;
        }
    }
}
