using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer
{
    public class LogLoadProgressEventArgs : EventArgs
    {
        public int Percent { get; }
        public string FileName { get; }
        public LogLoadProgressEventArgs(int percent, string fileName) { Percent = percent; FileName = fileName; }
    }

    public class LogLoadCompletedEventArgs : EventArgs
    {
        public List<LogEntry> Entries { get; }
        public string FileName { get; }
        public Exception Error { get; }
        public LogLoadCompletedEventArgs(List<LogEntry> entries, string fileName, Exception error = null)
        {
            Entries = entries; FileName = fileName; Error = error;
        }
    }

    public class LogLoader
    {
        // ── VEGA-D custom format: HH.MM.SS.mmm \t Level \t Source \t Message [\t CallStack]
        private static readonly Regex VegaPattern = new Regex(
            @"^(?<h>\d{2})\.(?<m>\d{2})\.(?<s>\d{2})\.(?<ms>\d{3})\t(?<lvl>\w+)\t(?<src>[^\t]+)\t(?<msg>[^\t]*)(?:\t(?<cs>.+))?$",
            RegexOptions.Compiled);

        // Generic: 2026-05-03 18:25:01 [ERROR] message
        private static readonly Regex GenericPattern = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:[.,]\d{1,3})?)\s*(?:\[(?<lvl>ERROR|WARN(?:ING)?|INFO|DEBUG|TRACE|FATAL)\]|(?<lvl2>ERROR|WARN(?:ING)?|INFO|DEBUG|TRACE|FATAL))\s*(?<src>\[[\w\.\-]+\])?\s*(?<msg>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Serilog: [18:25:01 ERR]
        private static readonly Regex SerilogPattern = new Regex(
            @"^\[(?<ts>\d{2}:\d{2}:\d{2})\s+(?<lvl>ERR|WRN|INF|DBG|FTL)\]\s+(?<msg>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LevelAnywhere = new Regex(
            @"(?<lvl>ERROR|WARN(?:ING)?|INFO|DEBUG|TRACE|FATAL)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss,fff",
            "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ssZ",
        };

        public event EventHandler<LogLoadProgressEventArgs> Progress;
        public event EventHandler<LogLoadCompletedEventArgs> Completed;

        private BackgroundWorker _worker;

        public void LoadAsync(string filePath)
        {
            _worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            _worker.DoWork += (s, e) => e.Result = Load(filePath, _worker, e);
            _worker.ProgressChanged += (s, e) =>
                Progress?.Invoke(this, new LogLoadProgressEventArgs(e.ProgressPercentage, filePath));
            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (e.Error != null)
                    Completed?.Invoke(this, new LogLoadCompletedEventArgs(null, filePath, e.Error));
                else
                    Completed?.Invoke(this, new LogLoadCompletedEventArgs(e.Result as List<LogEntry>, filePath));
            };
            _worker.RunWorkerAsync();
        }

        public void Cancel() => _worker?.CancelAsync();

        private List<LogEntry> Load(string filePath, BackgroundWorker worker, DoWorkEventArgs e)
        {
            var entries = new List<LogEntry>(4096);
            string fileName = Path.GetFileName(filePath);
            var info = new FileInfo(filePath);
            long totalBytes = info.Length;
            int lineNumber = 0;
            int lastPercent = -1;

            // Extract calendar date from file name (e.g. "2026-04-29_Total.txt")
            DateTime logDate = TryExtractDateFromFileName(fileName);

            using (var reader = new StreamReader(filePath, DetectEncoding(filePath), true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (worker.CancellationPending) { e.Cancel = true; return entries; }

                    lineNumber++;

                    int percent = totalBytes > 0 ? (int)(reader.BaseStream.Position * 100 / totalBytes) : 0;
                    if (percent != lastPercent)
                    {
                        worker.ReportProgress(percent);
                        lastPercent = percent;
                    }

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    entries.Add(ParseLine(line, lineNumber, fileName, logDate));
                }
            }

            return entries;
        }

        // ponytail: reads up to 4 KB to sniff encoding; ceiling = files with valid UTF-8 sequences
        // that are actually CP949 (extremely rare in practice). Upgrade: chardet or ICU if needed.
        private static Encoding DetectEncoding(string filePath)
        {
            byte[] buf = new byte[4096];
            int read;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                read = fs.Read(buf, 0, buf.Length);

            // BOM checks
            if (read >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) return Encoding.UTF8;
            if (read >= 2 && buf[0] == 0xFF && buf[1] == 0xFE) return Encoding.Unicode;
            if (read >= 2 && buf[0] == 0xFE && buf[1] == 0xFF) return Encoding.BigEndianUnicode;

            // Try decoding as UTF-8 without BOM; fall back to system default (CP949 on Korean Windows)
            try
            {
                new UTF8Encoding(false, true).GetString(buf, 0, read);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default;
            }
        }

        /// <summary>
        /// Extracts calendar date from file name.
        /// e.g. "2026-04-29_Total.txt" → 2026-04-29; fallback → today.
        /// </summary>
        private static DateTime TryExtractDateFromFileName(string fileName)
        {
            var m = Regex.Match(fileName, @"(\d{4})[-_\.](\d{2})[-_\.](\d{2})");
            if (m.Success &&
                int.TryParse(m.Groups[1].Value, out int y) &&
                int.TryParse(m.Groups[2].Value, out int mo) &&
                int.TryParse(m.Groups[3].Value, out int d))
            {
                try { return new DateTime(y, mo, d); } catch { }
            }
            return DateTime.Today;
        }

        public static LogEntry ParseLine(string line, int lineNumber, string fileName, DateTime logDate = default)
        {
            if (logDate == default) logDate = DateTime.Today;

            var entry = new LogEntry
            {
                LineNumber = lineNumber,
                RawLine = line,
                FileName = fileName,
                Level = "INFO",
                Message = line,
            };

            // ── 1. VEGA-D format: HH.MM.SS.mmm \t Level \t Source \t Message ──
            var m = VegaPattern.Match(line);
            if (m.Success)
            {
                if (int.TryParse(m.Groups["h"].Value, out int h) &&
                    int.TryParse(m.Groups["m"].Value, out int mi) &&
                    int.TryParse(m.Groups["s"].Value, out int s) &&
                    int.TryParse(m.Groups["ms"].Value, out int ms))
                {
                    entry.Timestamp = logDate.Date.AddHours(h).AddMinutes(mi).AddSeconds(s).AddMilliseconds(ms);
                }

                entry.Level = NormalizeLevel(m.Groups["lvl"].Value);
                entry.Source = m.Groups["src"].Value.Trim();
                entry.Message = m.Groups["msg"].Value.Trim();
                if (m.Groups["cs"].Success)
                    entry.CallStack = m.Groups["cs"].Value.Trim();
                return entry;
            }

            // ── 2. Generic structured format ──────────────────────────────────
            m = GenericPattern.Match(line);
            if (m.Success)
            {
                TryParseTimestamp(m.Groups["ts"].Value, out var ts);
                entry.Timestamp = ts;
                string lvl = m.Groups["lvl"].Success ? m.Groups["lvl"].Value : m.Groups["lvl2"].Value;
                entry.Level = NormalizeLevel(lvl);
                if (m.Groups["src"].Success) entry.Source = m.Groups["src"].Value.Trim('[', ']');
                entry.Message = m.Groups["msg"].Value.Trim();
                return entry;
            }

            // ── 3. Serilog ────────────────────────────────────────────────────
            m = SerilogPattern.Match(line);
            if (m.Success)
            {
                entry.Level = NormalizeLevel(m.Groups["lvl"].Value);
                entry.Message = m.Groups["msg"].Value.Trim();
                return entry;
            }

            // ── 4. Fallback: extract level keyword ────────────────────────────
            m = LevelAnywhere.Match(line);
            if (m.Success) entry.Level = NormalizeLevel(m.Groups["lvl"].Value);

            var tsMatch = Regex.Match(line, @"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}");
            if (tsMatch.Success && TryParseTimestamp(tsMatch.Value, out var ts2))
                entry.Timestamp = ts2;

            return entry;
        }

        private static bool TryParseTimestamp(string s, out DateTime result)
        {
            foreach (var fmt in DateFormats)
            {
                if (DateTime.TryParseExact(s, fmt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out result))
                    return true;
            }
            return DateTime.TryParse(s, out result);
        }

        private static string NormalizeLevel(string lvl)
        {
            if (string.IsNullOrEmpty(lvl)) return "INFO";
            switch (lvl.ToUpperInvariant())
            {
                case "ERR":
                case "ERROR":
                case "FATAL": return "ERROR";
                case "WRN":
                case "WARN":
                case "WARNING": return "WARN";
                case "DBG":
                case "DEBUG":
                case "TRACE": return "DEBUG";
                default: return "INFO";
            }
        }
    }
}
