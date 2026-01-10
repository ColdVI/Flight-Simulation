using FlightRadarAPI.Models;

namespace FlightRadarAPI.Physics
{
    /// <summary>
    /// Calculates aerodynamic forces (lift, drag, thrust) based on flight conditions
    /// and aircraft performance characteristics.
    /// </summary>
    public static class AerodynamicsCalculator
    {
        /// <summary>
        /// Calculates all aerodynamic forces and coefficients for current flight conditions.
        /// </summary>
        public static AeroForces CalculateForces(
            AircraftPerformance aircraft,
            double trueAirspeed,     // m/s
            double altitude,          // m
            double grossWeight,       // kg
            double angleOfAttack,     // radians
            double throttle,          // 0-1
            bool flapsExtended = false)
        {
            var atm = AtmosphereModel.GetAtmosphereAt(altitude);
            
            // Dynamic pressure: q = 0.5 * ρ * V²
            double dynamicPressure = 0.5 * atm.Density * trueAirspeed * trueAirspeed;
            
            // Lift coefficient (simplified linear model for small AoA)
            double clAlpha = 2 * Math.PI * aircraft.AspectRatio / (aircraft.AspectRatio + 2);
            double cl = clAlpha * angleOfAttack;
            double clMax = flapsExtended ? aircraft.ClMaxFlaps : aircraft.ClMax;
            cl = Math.Clamp(cl, -clMax, clMax);
            
            // Lift force: L = q * S * CL
            double lift = dynamicPressure * aircraft.WingArea * cl;
            
            // Drag coefficient (parabolic polar: CD = CD0 + CL²/(π*e*AR))
            double inducedDragFactor = 1.0 / (Math.PI * aircraft.OswaldFactor * aircraft.AspectRatio);
            double cd = aircraft.Cd0 + cl * cl * inducedDragFactor;
            
            // Add form drag for high angle of attack
            if (Math.Abs(angleOfAttack) > 0.15) // ~8.6 degrees
            {
                cd += 0.01 * Math.Pow(Math.Abs(angleOfAttack) - 0.15, 2);
            }
            
            // Compressibility drag (wave drag at transonic speeds)
            double mach = trueAirspeed / atm.SpeedOfSound;
            if (mach > 0.75)
            {
                double machFactor = Math.Pow((mach - 0.75) / 0.15, 2);
                cd += 0.02 * Math.Min(1.0, machFactor);
            }
            
            // Drag force: D = q * S * CD
            double drag = dynamicPressure * aircraft.WingArea * cd;
            
            // Thrust calculation with altitude lapse
            double thrustLapse = Math.Pow(atm.DensityRatio, aircraft.ThrustLapseRate);
            double maxThrust = aircraft.MaxThrustSeaLevel * thrustLapse;
            double thrust = maxThrust * throttle;
            
            // Fuel consumption
            double fuelFlow = thrust * aircraft.TSFC; // kg/s
            
            return new AeroForces
            {
                Lift = lift,
                Drag = drag,
                Thrust = thrust,
                DynamicPressure = dynamicPressure,
                LiftCoefficient = cl,
                DragCoefficient = cd,
                LiftToDrag = cl / cd,
                FuelFlowRate = fuelFlow,
                Mach = mach,
                TrueAirspeed = trueAirspeed,
                IndicatedAirspeed = AtmosphereModel.TasToIas(trueAirspeed, altitude)
            };
        }

        /// <summary>
        /// Calculates the required angle of attack for level flight at given conditions.
        /// </summary>
        public static double CalculateLevelFlightAoA(
            AircraftPerformance aircraft,
            double trueAirspeed,
            double altitude,
            double grossWeight)
        {
            var atm = AtmosphereModel.GetAtmosphereAt(altitude);
            double dynamicPressure = 0.5 * atm.Density * trueAirspeed * trueAirspeed;
            
            // Required lift = Weight
            double requiredLift = grossWeight * AtmosphereModel.GravitationalAcceleration;
            double requiredCl = requiredLift / (dynamicPressure * aircraft.WingArea);
            
            // Invert the CL formula
            double clAlpha = 2 * Math.PI * aircraft.AspectRatio / (aircraft.AspectRatio + 2);
            double aoa = requiredCl / clAlpha;
            
            return Math.Clamp(aoa, -0.26, 0.26); // Limit to ±15 degrees
        }

        /// <summary>
        /// Calculates stall speed for given weight and altitude.
        /// </summary>
        public static double CalculateStallSpeed(
            AircraftPerformance aircraft,
            double altitude,
            double grossWeight,
            bool flapsExtended = false)
        {
            var atm = AtmosphereModel.GetAtmosphereAt(altitude);
            double clMax = flapsExtended ? aircraft.ClMaxFlaps : aircraft.ClMax;
            double weight = grossWeight * AtmosphereModel.GravitationalAcceleration;
            
            // V = sqrt(2W / (ρ * S * CLmax))
            return Math.Sqrt(2 * weight / (atm.Density * aircraft.WingArea * clMax));
        }

        /// <summary>
        /// Calculates maximum sustainable climb rate at given conditions.
        /// </summary>
        public static double CalculateMaxClimbRate(
            AircraftPerformance aircraft,
            double trueAirspeed,
            double altitude,
            double grossWeight,
            double throttle = 1.0)
        {
            double aoa = CalculateLevelFlightAoA(aircraft, trueAirspeed, altitude, grossWeight);
            var forces = CalculateForces(aircraft, trueAirspeed, altitude, grossWeight, aoa, throttle);
            
            // Excess power = (T - D) * V
            double excessPower = (forces.Thrust - forces.Drag) * trueAirspeed;
            
            // Climb rate = Excess Power / Weight
            double weight = grossWeight * AtmosphereModel.GravitationalAcceleration;
            double climbRate = excessPower / weight;
            
            return Math.Max(0, climbRate);
        }

        /// <summary>
        /// Calculates optimal cruise speed for best range (Carson speed).
        /// </summary>
        public static double CalculateBestCruiseSpeed(
            AircraftPerformance aircraft,
            double altitude,
            double grossWeight)
        {
            var atm = AtmosphereModel.GetAtmosphereAt(altitude);
            double weight = grossWeight * AtmosphereModel.GravitationalAcceleration;
            
            // Best L/D occurs at CL = sqrt(CD0 * π * e * AR)
            double clBestLd = Math.Sqrt(aircraft.Cd0 * Math.PI * aircraft.OswaldFactor * aircraft.AspectRatio);
            
            // Carson speed is ~1.32 times the speed for best L/D (best range for jets)
            double speedBestLd = Math.Sqrt(2 * weight / (atm.Density * aircraft.WingArea * clBestLd));
            return speedBestLd * 1.32;
        }

        /// <summary>
        /// Calculates the bank angle required for a coordinated turn at given rate.
        /// </summary>
        public static double CalculateBankAngle(double trueAirspeed, double turnRate)
        {
            // tan(φ) = V * ω / g
            double tanPhi = trueAirspeed * turnRate / AtmosphereModel.GravitationalAcceleration;
            return Math.Atan(tanPhi);
        }

        /// <summary>
        /// Calculates the turn rate for a given bank angle.
        /// </summary>
        public static double CalculateTurnRate(double trueAirspeed, double bankAngle)
        {
            // ω = g * tan(φ) / V
            return AtmosphereModel.GravitationalAcceleration * Math.Tan(bankAngle) / trueAirspeed;
        }

        /// <summary>
        /// Calculates load factor for a given bank angle.
        /// </summary>
        public static double CalculateLoadFactor(double bankAngle)
        {
            return 1.0 / Math.Cos(bankAngle);
        }
    }

    /// <summary>
    /// Represents calculated aerodynamic forces and related values.
    /// </summary>
    public class AeroForces
    {
        /// <summary>Lift force in Newtons</summary>
        public double Lift { get; init; }
        
        /// <summary>Drag force in Newtons</summary>
        public double Drag { get; init; }
        
        /// <summary>Thrust force in Newtons</summary>
        public double Thrust { get; init; }
        
        /// <summary>Dynamic pressure in Pascals</summary>
        public double DynamicPressure { get; init; }
        
        /// <summary>Lift coefficient (CL)</summary>
        public double LiftCoefficient { get; init; }
        
        /// <summary>Drag coefficient (CD)</summary>
        public double DragCoefficient { get; init; }
        
        /// <summary>Lift-to-drag ratio (L/D)</summary>
        public double LiftToDrag { get; init; }
        
        /// <summary>Fuel flow rate in kg/s</summary>
        public double FuelFlowRate { get; init; }
        
        /// <summary>Mach number</summary>
        public double Mach { get; init; }
        
        /// <summary>True airspeed in m/s</summary>
        public double TrueAirspeed { get; init; }
        
        /// <summary>Indicated airspeed in m/s</summary>
        public double IndicatedAirspeed { get; init; }
        
        /// <summary>Net force along flight path (Thrust - Drag) in Newtons</summary>
        public double NetThrust => Thrust - Drag;
        
        /// <summary>Specific excess power (ft/min equivalent)</summary>
        public double SpecificExcessPower => NetThrust * TrueAirspeed;
    }
}
