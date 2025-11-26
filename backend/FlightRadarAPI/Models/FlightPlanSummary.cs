namespace FlightRadarAPI.Models
{
    public class FlightPlanSummary
    {
        public string Callsign { get; set; } = string.Empty;
        public string AircraftTail { get; set; } = string.Empty;
        public string OriginCode { get; set; } = string.Empty;
        public string DestinationCode { get; set; } = string.Empty;
        public double PlannedSpeedMs { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        public double Progress { get; set; }
    }
}
