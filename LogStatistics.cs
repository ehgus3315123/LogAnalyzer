using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LogAnalyzer
{
    public class KeywordCount
    {
        public string Keyword { get; set; }
        public int Count { get; set; }
    }

    public class TimePoint
    {
        public string Label { get; set; }
        public int ErrorCount { get; set; }
        public int WarnCount { get; set; }
        public int InfoCount { get; set; }
        public int Total => ErrorCount + WarnCount + InfoCount;
    }

    public class LogStatistics
    {
        public int TotalCount { get; private set; }
        public int ErrorCount { get; private set; }
        public int WarnCount { get; private set; }
        public int InfoCount { get; private set; }
        public int DebugCount { get; private set; }
        public double ErrorRate => TotalCount > 0 ? (double)ErrorCount / TotalCount * 100.0 : 0.0;
        public List<KeywordCount> TopKeywords { get; private set; } = new List<KeywordCount>();
        public List<TimePoint> Timeline { get; private set; } = new List<TimePoint>();

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the","and","or","in","on","at","to","a","an","of","for","is","are","was","were",
            "it","this","that","with","from","by","as","be","not","error","warn","info","debug",
            "null","true","false","new","class","void","int","string","object","return","list"
        };

        public void Calculate(IEnumerable<LogEntry> entries)
        {
            TotalCount = 0; ErrorCount = 0; WarnCount = 0; InfoCount = 0; DebugCount = 0;
            var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var hourBuckets = new SortedDictionary<string, TimePoint>();

            foreach (var entry in entries)
            {
                TotalCount++;
                string level = entry.LevelDisplay;
                switch (level)
                {
                    case "ERROR": ErrorCount++; break;
                    case "WARN": WarnCount++; break;
                    case "DEBUG": DebugCount++; break;
                    default: InfoCount++; break;
                }

                ExtractWords(entry.Message ?? entry.RawLine, wordCounts);

                if (entry.Timestamp.HasValue)
                {
                    string key = entry.Timestamp.Value.ToString("MM-dd HH:00");
                    if (!hourBuckets.ContainsKey(key))
                        hourBuckets[key] = new TimePoint { Label = key };
                    var tp = hourBuckets[key];
                    if (level == "ERROR") tp.ErrorCount++;
                    else if (level == "WARN") tp.WarnCount++;
                    else tp.InfoCount++;
                }
            }

            TopKeywords = wordCounts
                .Where(kv => kv.Value >= 2)
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .Select(kv => new KeywordCount { Keyword = kv.Key, Count = kv.Value })
                .ToList();

            var allPoints = hourBuckets.Values.ToList();
            Timeline = allPoints.Count > 24 ? SampleTimeline(allPoints, 24) : allPoints;
        }

        private static void ExtractWords(string text, Dictionary<string, int> counts)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match m in Regex.Matches(text, @"[A-Za-z][A-Za-z0-9]{3,}"))
            {
                string word = m.Value;
                if (!StopWords.Contains(word))
                {
                    counts.TryGetValue(word, out int c);
                    counts[word] = c + 1;
                }
            }
        }

        private static List<TimePoint> SampleTimeline(List<TimePoint> points, int maxPoints)
        {
            if (points.Count <= maxPoints) return points;
            int step = (int)Math.Ceiling((double)points.Count / maxPoints);
            var result = new List<TimePoint>();
            for (int i = 0; i < points.Count; i += step)
            {
                int end = Math.Min(i + step, points.Count);
                var merged = new TimePoint { Label = points[i].Label };
                for (int j = i; j < end; j++)
                {
                    merged.ErrorCount += points[j].ErrorCount;
                    merged.WarnCount += points[j].WarnCount;
                    merged.InfoCount += points[j].InfoCount;
                }
                result.Add(merged);
            }
            return result;
        }
    }
}
