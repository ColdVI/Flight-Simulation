namespace FlightRadarAPI.Models
{
    public class Flight
    {
        public string Callsign { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string OriginName { get; set; } = string.Empty;
        public string DestinationName { get; set; } = string.Empty;
        public string AircraftTail { get; set; } = string.Empty;
        public string AircraftModel { get; set; } = string.Empty;
        public string AircraftManufacturer { get; set; } = string.Empty;
        
        // Anlık Konum Verileri
        public double CurrentLat { get; set; }
        public double CurrentLon { get; set; }
        public double Altitude { get; set; }
        public double Heading { get; set; }
        public double SpeedMs { get; set; }
        public string Status { get; set; } = "WAITING"; 

        // Simülasyon İçin Rota Verileri
        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        public double DestLat { get; set; }
        public double DestLon { get; set; }
        
        public DateTime StartTime { get; set; }
        public int StartOffsetSeconds { get; set; }
        public double Progress { get; set; } // 0.0 ile 1.0 arası ilerleme
    }
}