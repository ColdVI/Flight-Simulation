using System.Collections.Concurrent;
using FlightRadarAPI.Models;
using Microsoft.Extensions.Logging;

namespace FlightRadarAPI.Services
{
    /// <summary>
    /// Records telemetry data during flight simulation for post-flight analysis and reporting.
    /// </summary>
    public class FlightDataRecorder
    {
        private readonly ILogger<FlightDataRecorder> _logger;
        private readonly ConcurrentDictionary<string, FlightRecording> _recordings = new();
        private readonly ConcurrentDictionary<string, FlightReport> _completedReports = new();
        private readonly TimeSpan _sampleInterval = TimeSpan.FromSeconds(1);
        
        public FlightDataRecorder(ILogger<FlightDataRecorder> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Records a telemetry sample for a flight if the sample interval has elapsed.
        /// </summary>
        public void RecordSample(Flight flight)
        {
            if (flight.Phase == FlightPhase.Preflight || flight.Phase == FlightPhase.Arrived)
            {
                return;
            }
            
            var recording = _recordings.GetOrAdd(flight.Callsign, _ => new FlightRecording
            {
                Callsign = flight.Callsign,
                StartedAt = DateTime.UtcNow
            });
            
            var now = DateTime.UtcNow;
            
            // Only record at the specified interval
            if (now - recording.LastSampleTime < _sampleInterval)
            {
                return;
            }
            
            var sample = FlightTelemetrySample.FromFlight(flight, now);
            recording.Samples.Add(sample);
            recording.LastSampleTime = now;
            
            // Check if flight has completed
            if (flight.Phase == FlightPhase.Arrived)
            {
                GenerateReport(flight);
            }
        }
        
        /// <summary>
        /// Finalizes the recording for a completed flight and generates the report.
        /// </summary>
        public FlightReport? GenerateReport(Flight flight)
        {
            if (!_recordings.TryRemove(flight.Callsign, out var recording))
            {
                _logger.LogWarning("No recording found for flight {Callsign}", flight.Callsign);
                return null;
            }
            
            _logger.LogInformation(
                "Generating report for flight {Callsign}: {Samples} samples over {Duration}",
                flight.Callsign, 
                recording.Samples.Count,
                TimeSpan.FromSeconds(flight.FlightTimeSeconds));
            
            var report = FlightReport.Generate(flight, recording.Samples);
            _completedReports[flight.Callsign] = report;
            
            return report;
        }
        
        /// <summary>
        /// Gets the report for a completed flight.
        /// </summary>
        public FlightReport? GetReport(string callsign)
        {
            _completedReports.TryGetValue(callsign, out var report);
            return report;
        }
        
        /// <summary>
        /// Gets all completed flight reports.
        /// </summary>
        public IReadOnlyList<FlightReport> GetAllReports()
        {
            return _completedReports.Values.ToList();
        }
        
        /// <summary>
        /// Gets the current recording state for an active flight (for monitoring).
        /// </summary>
        public (int SampleCount, TimeSpan Duration)? GetRecordingStatus(string callsign)
        {
            if (!_recordings.TryGetValue(callsign, out var recording))
            {
                return null;
            }
            
            return (recording.Samples.Count, DateTime.UtcNow - recording.StartedAt);
        }
        
        /// <summary>
        /// Exports a report to JSON.
        /// </summary>
        public string? ExportReportJson(string callsign, bool includeTelemetry = false)
        {
            var report = GetReport(callsign);
            return report?.ToJson(includeTelemetry);
        }
        
        /// <summary>
        /// Exports telemetry data to CSV.
        /// </summary>
        public string? ExportTelemetryCsv(string callsign)
        {
            var report = GetReport(callsign);
            return report?.TelemetryToCsv();
        }
        
        /// <summary>
        /// Clears a specific report from memory.
        /// </summary>
        public bool ClearReport(string callsign)
        {
            return _completedReports.TryRemove(callsign, out _);
        }
        
        /// <summary>
        /// Clears all reports from memory.
        /// </summary>
        public void ClearAllReports()
        {
            _completedReports.Clear();
            _logger.LogInformation("Cleared all flight reports from memory");
        }
        
        /// <summary>
        /// Resets all recordings (called when simulation is reset).
        /// </summary>
        public void ResetRecordings()
        {
            _recordings.Clear();
            _logger.LogInformation("Cleared all active flight recordings");
        }
        
        private class FlightRecording
        {
            public string Callsign { get; set; } = string.Empty;
            public DateTime StartedAt { get; set; }
            public DateTime LastSampleTime { get; set; }
            public List<FlightTelemetrySample> Samples { get; } = new();
        }
    }
}
