using System;
using System.Collections.Generic;

namespace LogAnalyzer
{
    public class LogFilter
    {
        /// <summary>
        /// 칩이 1개 이상이면 Message/Source 중 하나라도 포함(OR)해야 표시.
        /// 비어있으면 <see cref="SearchKeyword"/> 실시간 필터 적용.
        /// </summary>
        public IReadOnlyList<string> IncludeChips { get; set; } = Array.Empty<string>();

        /// <summary>칩이 없을 때 실시간 키워드 필터.</summary>
        public string SearchKeyword { get; set; } = string.Empty;

        /// <summary>이 문자열이 포함된 메시지는 숨김. 비어있으면 제외 조건 없음.</summary>
        public string ExcludeKeyword { get; set; } = string.Empty;

        /// <summary>칩이 1개 이상이면 해당 키워드를 포함하는 항목은 숨김.</summary>
        public IReadOnlyList<string> ExcludeChips { get; set; } = Array.Empty<string>();

        public bool IsMatch(LogEntry entry)
        {
            if (entry == null) return false;

            var msg = entry.Message ?? string.Empty;
            var src = entry.Source ?? string.Empty;

            // 포함 조건
            if (IncludeChips != null && IncludeChips.Count > 0)
            {
                bool anyMatch = false;
                foreach (var chip in IncludeChips)
                {
                    if (msg.IndexOf(chip, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        src.IndexOf(chip, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        anyMatch = true;
                        break;
                    }
                }
                if (!anyMatch) return false;
            }
            else if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                if (msg.IndexOf(SearchKeyword, StringComparison.OrdinalIgnoreCase) < 0 &&
                    src.IndexOf(SearchKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // 제외 조건
            if (ExcludeChips != null && ExcludeChips.Count > 0)
            {
                foreach (var chip in ExcludeChips)
                {
                    if (msg.IndexOf(chip, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        src.IndexOf(chip, StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(ExcludeKeyword))
            {
                if (msg.IndexOf(ExcludeKeyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    src.IndexOf(ExcludeKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            return true;
        }
    }
}
