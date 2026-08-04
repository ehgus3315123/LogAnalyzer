using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LogAnalyzer
{
    public static class RecentFilesService
    {
        private const int MaxItems = 10;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogAnalyzer",
            "recent_files.json");

        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(List<RecentFileItem>));

        public static List<RecentFileItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<RecentFileItem>();
                using (var fs = File.OpenRead(FilePath))
                    return ((List<RecentFileItem>)Serializer.ReadObject(fs))
                           ?? new List<RecentFileItem>();
            }
            catch
            {
                return new List<RecentFileItem>();
            }
        }

        public static void Add(string path)
        {
            var list = Load();
            list.RemoveAll(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new RecentFileItem { FilePath = path, LastOpened = DateTime.Now });
            if (list.Count > MaxItems) list.RemoveRange(MaxItems, list.Count - MaxItems);
            Save(list);
        }

        public static void Remove(string path)
        {
            var list = Load();
            if (list.RemoveAll(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)) > 0)
                Save(list);
        }

        public static void Clear() => Save(new List<RecentFileItem>());

        private static void Save(List<RecentFileItem> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var fs = File.Create(FilePath))
                    Serializer.WriteObject(fs, list);
            }
            catch { /* ponytail: 저장 실패는 무시 — 기능 동작에 영향 없음 */ }
        }
    }
}
