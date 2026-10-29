using System;

namespace LogViewer
{
    public class LogEntry
    {
        // ponytail: 스크롤 때마다 바인딩이 반복 호출하므로 문자열 변환 결과를 캐시한다.
        private string _level, _levelDisplay;

        public int LineNumber { get; set; }
        public DateTime? Timestamp { get; set; }
        public string Level
        {
            get => _level;
            set { _level = value; _levelDisplay = null; }
        }
        public string Source { get; set; }
        public string Message { get; set; }
        public string RawLine { get; set; }
        public string FileName { get; set; }
        public string CallStack { get; set; }

        /// <summary>로그 원문에 적힌 시각 문자열 그대로.</summary>
        public string TimestampText { get; set; }
        public string TimestampDisplay => TimestampText ?? string.Empty;

        // ponytail: 목록 컬럼(폭 100)이 좁아 "2026-10-20 09:00:25"가 잘린다.
        // 목록엔 시간만, 전체(날짜 포함)는 TimestampDisplay로 상세/복사/툴팁에서 쓴다.
        public string TimeDisplay
        {
            get
            {
                var t = TimestampText ?? string.Empty;
                int i = t.IndexOfAny(new[] { ' ', 'T' });
                return i > 0 && i + 1 < t.Length ? t.Substring(i + 1) : t;
            }
        }

        public string LevelDisplay =>
            _levelDisplay ?? (_levelDisplay = string.IsNullOrEmpty(_level) ? "INFO" : _level.ToUpperInvariant());

        // ponytail: 매우 긴 줄은 TextBlock 측정/렌더 비용이 커서 표시용만 자른다(전체는 툴팁/복사에 그대로).
        // 천장: 컬럼 폭이 매우 넓어지면 잘려 보임 → 상한 상향.
        private const int DisplayMax = 400;
        public string MessageDisplay => Clip(Message);
        public string CallStackDisplay => Clip(CallStack);
        private static string Clip(string s) => s != null && s.Length > DisplayMax ? s.Substring(0, DisplayMax) : s;
    }
}
