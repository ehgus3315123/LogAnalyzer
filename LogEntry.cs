using System;

namespace LogAnalyzer
{
    public class LogEntry
    {
        public int LineNumber { get; set; }
        public DateTime? Timestamp { get; set; }
        public string Level { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public string RawLine { get; set; }
        public string FileName { get; set; }
        public string CallStack { get; set; }

        public string TimestampDisplay =>
            Timestamp.HasValue ? Timestamp.Value.ToString("HH:mm:ss") : string.Empty;
        public string TimeDisplay =>
            Timestamp.HasValue ? Timestamp.Value.ToString("HH:mm:ss") : string.Empty;

        public string LevelDisplay => string.IsNullOrEmpty(Level) ? "INFO" : Level.ToUpperInvariant();
    }
}
