using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace LogAnalyzer
{
    public static class RecentSearchesService
    {
        private const int MaxItems = 15;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogAnalyzer",
            "recent_searches.json");

        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(List<RecentSearchItem>));

        public static List<RecentSearchItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<RecentSearchItem>();
                using (var fs = File.OpenRead(FilePath))
                    return ((List<RecentSearchItem>)Serializer.ReadObject(fs))
                           ?? new List<RecentSearchItem>();
            }
            catch
            {
                return new List<RecentSearchItem>();
            }
        }

        public static void Add(string text)
        {
            var list = Load();
            list.RemoveAll(x => string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new RecentSearchItem { Text = text, LastUsed = DateTime.Now });
            if (list.Count > MaxItems) list.RemoveRange(MaxItems, list.Count - MaxItems);
            Save(list);
        }

        public static void Remove(string text)
        {
            var list = Load();
            if (list.RemoveAll(x => string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase)) > 0)
                Save(list);
        }

        public static void Clear() => Save(new List<RecentSearchItem>());

        private static void Save(List<RecentSearchItem> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var fs = File.Create(FilePath))
                    Serializer.WriteObject(fs, list);
            }
            catch { /* ponytail: 저장 실패 무시 — 최근 검색어는 편의 기능 */ }
        }
    }
}
