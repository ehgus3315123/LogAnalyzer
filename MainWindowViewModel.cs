using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace LogViewer
{
    public class FilterChip
    {
        public string Keyword { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // ── Backing fields ────────────────────────────────────────────────
        private string _searchKeyword = string.Empty;
        private string _excludeKeyword = string.Empty;
        private bool _isLoading;
        private int _loadProgress;
        private string _statusText = "로그 파일을 불러오세요";
        private bool _isDarkMode;
        private LogEntry _selectedEntry;
        private bool _isStartupScreenVisible = true;
        private bool _isDragOver;
        private bool _isDragOverValid;

        // ── Collections ──────────────────────────────────────────────────
        private readonly RangeObservableCollection<LogEntry> _allEntries = new RangeObservableCollection<LogEntry>();

        // ── Recent files ─────────────────────────────────────────────────
        public ObservableCollection<RecentFileItem> RecentFiles { get; } = new ObservableCollection<RecentFileItem>();
        public bool HasRecentFiles => RecentFiles.Count > 0;
        public ListCollectionView FilteredView { get; }
        public ObservableCollection<string> LoadedFiles { get; } = new ObservableCollection<string>();
        public ObservableCollection<FilterChip> FilterChips { get; } = new ObservableCollection<FilterChip>();
        public ObservableCollection<FilterChip> ExcludeChips { get; } = new ObservableCollection<FilterChip>();
        public ObservableCollection<SourceExcludeItem> SourceFilters { get; } = new ObservableCollection<SourceExcludeItem>();

        // ── Statistics (카운트만 유지 — 하단 바 표시용) ──────────────────
        private int _totalCount, _errorCount, _warnCount;

        // ── Loaders in progress ───────────────────────────────────────────
        private readonly List<LogLoader> _activeLoaders = new List<LogLoader>();
        private int _pendingLoads;

        // ── Filter object (stateless snapshot, rebuilt on change) ─────────
        private readonly LogFilter _filter = new LogFilter();

        private static readonly string FilterFolder = @"C:\PDHLogFilter";
        private static readonly string IncludeFile = Path.Combine(FilterFolder, "include.txt");
        private static readonly string ExcludeFile = Path.Combine(FilterFolder, "exclude.txt");
        private static readonly string SourcesExcludeFile = Path.Combine(FilterFolder, "sources_exclude.txt");
        private bool _suppressFilterSave;
        private bool _suppressSourceFilterCallback;
        private HashSet<string> _excludedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ALLOWED_EXTENSIONS =
            new HashSet<string>(new[] { ".log", ".txt" }, StringComparer.OrdinalIgnoreCase);

        public MainWindowViewModel()
        {
            FilteredView = new ListCollectionView(_allEntries);
            FilteredView.Filter = FilterPredicate;

            OpenFileCommand = new RelayCommand(OpenFile);
            ClearFilesCommand = new RelayCommand(ClearFiles, () => _allEntries.Count > 0 && !_isLoading);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => FilteredCount > 0);
            ExportTxtCommand = new RelayCommand(ExportTxt, () => FilteredCount > 0);
            ToggleDarkModeCommand = new RelayCommand(ToggleDarkMode);
            ResetToStartupCommand = new RelayCommand(_ => ResetToStartup());
            AddFilterChipCommand = new RelayCommand(AddFilterChip);
            AddExcludeChipCommand = new RelayCommand(AddExcludeChip);
            SelectAllSourcesCommand = new RelayCommand(SelectAllSources, () => SourceFilters.Count > 0);
            DeselectAllSourcesCommand = new RelayCommand(DeselectAllSources, () => SourceFilters.Count > 0);
            OpenRecentFileCommand = new RelayCommand(OpenRecentFile);
            ClearRecentFilesCommand = new RelayCommand(ClearRecentFiles);
            RefreshFilesCommand = new RelayCommand(RefreshLoadedFiles, () => LoadedFiles.Count > 0 && !_isLoading);

            FilterChips.CollectionChanged += (_, __) => { SaveFilterChips(); ApplyFilter(); };
            ExcludeChips.CollectionChanged += (_, __) => { SaveExcludeChips(); ApplyFilter(); };
            RecentFiles.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasRecentFiles));

            LoadExcludedSources();
            LoadFilterChips();
            RefreshRecentFiles();
        }

        // ── Public Properties ─────────────────────────────────────────────
        public string SearchKeyword
        {
            get => _searchKeyword;
            set
            {
                if (_searchKeyword == value) return;
                _searchKeyword = value;
                OnPropertyChanged();
            }
        }

        public string ExcludeKeyword
        {
            get => _excludeKeyword;
            set
            {
                if (_excludeKeyword == value) return;
                _excludeKeyword = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); }
        }

        public int LoadProgress
        {
            get => _loadProgress;
            private set { _loadProgress = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsDarkMode
        {
            get => _isDarkMode;
            private set { _isDarkMode = value; OnPropertyChanged(); }
        }

        public LogEntry SelectedEntry
        {
            get => _selectedEntry;
            set { _selectedEntry = value; OnPropertyChanged(); }
        }

        public bool IsStartupScreenVisible
        {
            get => _isStartupScreenVisible;
            private set
            {
                _isStartupScreenVisible = value;
                OnPropertyChanged();
            }
        }

        public bool IsDragOver
        {
            get => _isDragOver;
            set { if (_isDragOver == value) return; _isDragOver = value; OnPropertyChanged(); }
        }

        public bool IsDragOverValid
        {
            get => _isDragOverValid;
            set { if (_isDragOverValid == value) return; _isDragOverValid = value; OnPropertyChanged(); }
        }

        public int TotalCount { get => _totalCount; private set { _totalCount = value; OnPropertyChanged(); UpdateStatus(); } }
        public int ErrorCount { get => _errorCount; private set { _errorCount = value; OnPropertyChanged(); } }
        public int WarnCount { get => _warnCount; private set { _warnCount = value; OnPropertyChanged(); } }

        public int FilteredCount => FilteredView?.Count ?? 0;
        public bool IsEmpty => !_isLoading && FilteredCount == 0;
        public bool HasSourceFilters => SourceFilters.Count > 0;

        // ── Commands ──────────────────────────────────────────────────────
        public ICommand OpenFileCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportTxtCommand { get; }
        public ICommand ToggleDarkModeCommand { get; }
        public ICommand ResetToStartupCommand { get; }
        public ICommand AddFilterChipCommand { get; }
        public ICommand AddExcludeChipCommand { get; }
        public ICommand SelectAllSourcesCommand { get; }
        public ICommand DeselectAllSourcesCommand { get; }
        public ICommand OpenRecentFileCommand { get; }
        public ICommand ClearRecentFilesCommand { get; }
        public ICommand RefreshFilesCommand { get; }

        // ── FilterChip ────────────────────────────────────────────────────
        private void AddFilterChip()
        {
            var kw = (_searchKeyword ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(kw)) return;
            if (FilterChips.Any(c => string.Equals(c.Keyword, kw, StringComparison.OrdinalIgnoreCase))) return;

            FilterChip chip = null;
            chip = new FilterChip
            {
                Keyword = kw,
                RemoveCommand = new RelayCommand(() => RemoveFilterChip(chip))
            };
            FilterChips.Add(chip);
            SearchKeyword = string.Empty;
        }

        private void RemoveFilterChip(FilterChip chip)
        {
            if (chip == null) return;
            FilterChips.Remove(chip);
        }

        private void AddExcludeChip()
        {
            var kw = (_excludeKeyword ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(kw)) return;
            if (ExcludeChips.Any(c => string.Equals(c.Keyword, kw, StringComparison.OrdinalIgnoreCase))) return;

            FilterChip chip = null;
            chip = new FilterChip
            {
                Keyword = kw,
                RemoveCommand = new RelayCommand(() => RemoveExcludeChip(chip))
            };
            ExcludeChips.Add(chip);
            ExcludeKeyword = string.Empty;
        }

        private void RemoveExcludeChip(FilterChip chip)
        {
            if (chip == null) return;
            ExcludeChips.Remove(chip);
        }

        // ── Recent files ─────────────────────────────────────────────────
        private void RefreshRecentFiles()
        {
            RecentFiles.Clear();
            foreach (var item in RecentFilesService.Load())
                RecentFiles.Add(item);
        }

        private void RecordRecentFile(string path)
        {
            RecentFilesService.Add(path);
            RefreshRecentFiles();
        }

        private void OpenRecentFile(object param)
        {
            var item = param as RecentFileItem;
            if (item == null) return;

            if (!File.Exists(item.FilePath))
            {
                MessageBox.Show($"파일을 찾을 수 없습니다:\n{item.FilePath}", "파일 없음",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                RecentFilesService.Remove(item.FilePath);
                RefreshRecentFiles();
                return;
            }

            OpenLoadedFile(item.FilePath);
        }

        private void ClearRecentFiles()
        {
            RecentFilesService.Clear();
            RefreshRecentFiles();
        }

        // ── Filter persistence ────────────────────────────────────────────
        private void LoadFilterChips()
        {
            Directory.CreateDirectory(FilterFolder);

            _suppressFilterSave = true;
            try
            {
                foreach (var kw in ReadKeywords(IncludeFile))
                    AddChipToCollection(FilterChips, kw, RemoveFilterChip);

                var excludeKeywords = ReadKeywords(ExcludeFile).ToList();
                if (!excludeKeywords.Any())
                    excludeKeywords = new List<string> { "Q:A1" };
                foreach (var kw in excludeKeywords)
                    AddChipToCollection(ExcludeChips, kw, RemoveExcludeChip);
            }
            finally
            {
                _suppressFilterSave = false;
            }
        }

        private static IEnumerable<string> ReadKeywords(string path)
        {
            if (!File.Exists(path)) return Enumerable.Empty<string>();
            return File.ReadAllLines(path, Encoding.UTF8)
                       .Select(l => l.Trim())
                       .Where(l => !string.IsNullOrEmpty(l))
                       .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private void AddChipToCollection(ObservableCollection<FilterChip> collection, string kw,
                                         Action<FilterChip> removeAction)
        {
            if (collection.Any(c => string.Equals(c.Keyword, kw, StringComparison.OrdinalIgnoreCase))) return;
            FilterChip chip = null;
            chip = new FilterChip { Keyword = kw, RemoveCommand = new RelayCommand(() => removeAction(chip)) };
            collection.Add(chip);
        }

        private void SaveFilterChips()  { if (!_suppressFilterSave) SaveKeywords(IncludeFile,  FilterChips); }
        private void SaveExcludeChips() { if (!_suppressFilterSave) SaveKeywords(ExcludeFile, ExcludeChips); }

        private static void SaveKeywords(string path, IEnumerable<FilterChip> chips)
        {
            try
            {
                Directory.CreateDirectory(FilterFolder);
                File.WriteAllLines(path, chips.Select(c => c.Keyword), Encoding.UTF8);
            }
            catch { /* 저장 실패는 무시 — 필터 동작에는 영향 없음 */ }
        }

        private void LoadExcludedSources()
        {
            _excludedSources = new HashSet<string>(ReadKeywords(SourcesExcludeFile), StringComparer.OrdinalIgnoreCase);
        }

        private void SaveExcludedSources()
        {
            if (_suppressSourceFilterCallback) return;
            try
            {
                Directory.CreateDirectory(FilterFolder);
                File.WriteAllLines(SourcesExcludeFile, _excludedSources.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), Encoding.UTF8);
            }
            catch { /* 저장 실패는 무시 — 필터 동작에는 영향 없음 */ }
        }

        private void RefreshSourceFilters()
        {
            _suppressSourceFilterCallback = true;
            try
            {
                SourceFilters.Clear();
                foreach (var group in _allEntries
                    .GroupBy(e => e.Source ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string source = group.Key;
                    bool included = !_excludedSources.Contains(source);
                    SourceFilters.Add(new SourceExcludeItem(source, group.Count(), included, OnSourceInclusionChanged));
                }
            }
            finally
            {
                _suppressSourceFilterCallback = false;
            }

            OnPropertyChanged(nameof(HasSourceFilters));
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnSourceInclusionChanged()
        {
            if (_suppressSourceFilterCallback) return;
            SyncExcludedSourcesFromUi();
            SaveExcludedSources();
            ApplyFilter();
        }

        private void SyncExcludedSourcesFromUi()
        {
            _excludedSources = new HashSet<string>(
                SourceFilters.Where(s => !s.IsIncluded).Select(s => s.Source),
                StringComparer.OrdinalIgnoreCase);
        }

        private void SelectAllSources()
        {
            _suppressSourceFilterCallback = true;
            try
            {
                foreach (var item in SourceFilters)
                    item.IsIncluded = true;
            }
            finally
            {
                _suppressSourceFilterCallback = false;
            }

            _excludedSources.Clear();
            SaveExcludedSources();
            ApplyFilter();
        }

        private void DeselectAllSources()
        {
            _suppressSourceFilterCallback = true;
            try
            {
                foreach (var item in SourceFilters)
                    item.IsIncluded = false;
            }
            finally
            {
                _suppressSourceFilterCallback = false;
            }

            SyncExcludedSourcesFromUi();
            SaveExcludedSources();
            ApplyFilter();
        }

        // ── Command Implementations ───────────────────────────────────────
        private void OpenFile(object _)
        {
            var dlg = new OpenFileDialog
            {
                Title = "로그 파일 선택",
                Filter = "로그 파일 (*.log;*.txt)|*.log;*.txt|모든 파일 (*.*)|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;
            OpenLoadedFile(dlg.FileName);
        }

        /// <summary>
        /// 시작 화면에서 단일 파일 드래그 앤 드롭 처리.
        /// </summary>
        public void DropSingleFileOnStartup(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var ext = Path.GetExtension(path);
            if (!ALLOWED_EXTENSIONS.Contains(ext))
            {
                MessageBox.Show(
                    $"지원하지 않는 파일 형식입니다: {ext}\n(.log, .txt 파일만 분석할 수 있습니다)",
                    "파일 형식 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            OpenLoadedFile(path);
        }

        /// <summary>
        /// 드래그 대상 파일이 허용된 확장자인지 검사한다.
        /// </summary>
        public static bool IsAllowedFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return ALLOWED_EXTENSIONS.Contains(Path.GetExtension(path));
        }

        /// <summary>파일 경로를 받아 로드합니다(파일 대화상자, 드래그 앤 드롭 등).</summary>
        public void AddFilePaths(IEnumerable<string> paths)
        {
            if (paths == null) return;

            string selected = null;
            foreach (var raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    if (!File.Exists(raw)) continue;
                    selected = Path.GetFullPath(raw);
                    break;
                }
                catch
                {
                    // Ignore invalid paths from drag/drop or dialog.
                }
            }

            if (selected == null) return;
            OpenLoadedFile(selected);
        }

        private void OpenLoadedFile(string path)
        {
            IsStartupScreenVisible = false;
            ClearFiles();
            LoadedFiles.Add(path);
            LoadFile(path);
        }

        private void LoadFile(string path)
        {
            IsLoading = true;
            _pendingLoads++;

            var loader = new LogLoader();
            _activeLoaders.Add(loader);

            loader.Progress += (s, e) => Application.Current?.Dispatcher.Invoke(() =>
            {
                LoadProgress = e.Percent;
                StatusText = $"로딩 중: {Path.GetFileName(e.FileName)} ({e.Percent}%)";
            });

            loader.Completed += (s, e) => Application.Current?.Dispatcher.Invoke(() =>
            {
                _activeLoaders.Remove(loader);
                _pendingLoads--;

                if (e.Error != null)
                {
                    MessageBox.Show($"파일 로드 실패:\n{e.Error.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else if (e.Entries != null)
                {
                    LoadProgress = 100;
                    StatusText = $"로딩 마무리 중: {Path.GetFileName(e.FileName)} (목록 반영)";
                    _allEntries.AddRange(e.Entries);
                    RecordRecentFile(e.FileName);
                }

                if (_pendingLoads == 0)
                {
                    IsLoading = false;
                    LoadProgress = 100;
                    RefreshStatistics();
                    RefreshSourceFilters();
                    FilteredView.Refresh();
                    OnPropertyChanged(nameof(FilteredCount));
                    OnPropertyChanged(nameof(IsEmpty));
                    UpdateStatus();
                }
            });

            loader.LoadAsync(path);
        }

        private void ClearFiles()
        {
            foreach (var loader in _activeLoaders.ToList())
                loader.Cancel();
            _activeLoaders.Clear();
            _pendingLoads = 0;
            _allEntries.Clear();
            LoadedFiles.Clear();
            RefreshStatistics();
            RefreshSourceFilters();
            FilteredView.Refresh();
            OnPropertyChanged(nameof(FilteredCount));
            StatusText = "로그 파일을 불러오세요";
            LoadProgress = 0;
            IsLoading = false;
        }

        /// <summary>현재 로드된 파일을 디스크에서 다시 읽습니다. 필터는 유지합니다.</summary>
        private void RefreshLoadedFiles()
        {
            var paths = LoadedFiles.ToList();
            if (paths.Count == 0) return;

            if (!paths.Any(File.Exists))
            {
                MessageBox.Show(
                    "다시 불러올 파일이 없습니다.\n파일이 이동되었거나 삭제되었을 수 있습니다.",
                    "새로고침",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            OpenLoadedFile(paths[0]);
        }

        private void ResetToStartup()
        {
            ClearFiles();
            IsStartupScreenVisible = true;
            _suppressFilterSave = true;
            FilterChips.Clear();
            ExcludeChips.Clear();
            _suppressFilterSave = false;
            LoadFilterChips();
            SearchKeyword = string.Empty;
            ExcludeKeyword = string.Empty;
        }

        private void ExportCsv()
        {
            var dlg = new SaveFileDialog
            {
                Title = "CSV로보내기 (표시 결과 전체)",
                Filter = "CSV 파일 (*.csv)|*.csv",
                FileName = $"log_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                WriteCsvFile(dlg.FileName, FilteredView.Cast<LogEntry>());
                StatusText = $"CSV 저장 완료: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportTxt()
        {
            var dlg = new SaveFileDialog
            {
                Title = "TXT로보내기 (표시 결과 전체)",
                Filter = "텍스트 파일 (*.txt)|*.txt",
                FileName = $"log_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var lines = FilteredView.Cast<LogEntry>().SelectMany(ToTxtLines);
                File.WriteAllLines(dlg.FileName, lines, Encoding.UTF8);
                StatusText = $"TXT 저장 완료: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>선택한 항목만 CSV로 저장합니다. 목록에 보이는 순서를 유지합니다.</summary>
        public void ExportSelectedCsv(IEnumerable<LogEntry> selected)
        {
            var ordered = OrderSelectedByFilteredView(selected);
            if (ordered.Count == 0)
            {
                MessageBox.Show("보낼 항목을 로그 목록에서 하나 이상 선택하세요.\n(Ctrl·Shift로 여러 줄 선택)", "선택보내기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "선택 항목 CSV로 저장",
                Filter = "CSV 파일 (*.csv)|*.csv",
                FileName = $"log_selected_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                WriteCsvFile(dlg.FileName, ordered);
                StatusText = $"선택 CSV 저장 완료 ({ordered.Count:N0}건): {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>선택한 항목의 원문 줄만 TXT로 저장합니다. 목록에 보이는 순서를 유지합니다.</summary>
        public void ExportSelectedTxt(IEnumerable<LogEntry> selected)
        {
            var ordered = OrderSelectedByFilteredView(selected);
            if (ordered.Count == 0)
            {
                MessageBox.Show("보낼 항목을 로그 목록에서 하나 이상 선택하세요.\n(Ctrl·Shift로 여러 줄 선택)", "선택보내기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "선택 항목 TXT로 저장",
                Filter = "텍스트 파일 (*.txt)|*.txt",
                FileName = $"log_selected_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var lines = ordered.SelectMany(ToTxtLines);
                File.WriteAllLines(dlg.FileName, lines, Encoding.UTF8);
                StatusText = $"선택 TXT 저장 완료 ({ordered.Count:N0}건): {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private IReadOnlyList<LogEntry> OrderSelectedByFilteredView(IEnumerable<LogEntry> selected)
        {
            if (selected == null) return Array.Empty<LogEntry>();
            var set = new HashSet<LogEntry>(selected);
            return FilteredView.Cast<LogEntry>().Where(e => set.Contains(e)).ToList();
        }

        private static void WriteCsvFile(string path, IEnumerable<LogEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("LineNumber,Timestamp,Level,Source,Message,CallStack");
            foreach (var e in entries)
            {
                sb.AppendLine($"{e.LineNumber}," +
                              $"\"{e.TimestampDisplay}\"," +
                              $"{e.LevelDisplay}," +
                              $"\"{e.Source}\"," +
                              $"\"{EscapeCsv(e.Message)}\"," +
                              $"\"{EscapeCsv(e.CallStack)}\"");
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static IEnumerable<string> ToTxtLines(LogEntry e)
        {
            yield return e.RawLine;
        }

        private void ToggleDarkMode()
        {
            IsDarkMode = !IsDarkMode;
            ThemeManager.Apply(IsDarkMode ? "Dark" : "Light");
        }

        // ── Filter ────────────────────────────────────────────────────────
        private void ApplyFilter()
        {
            _filter.IncludeChips = FilterChips.Select(c => c.Keyword).ToList();
            _filter.SearchKeyword = _searchKeyword;
            _filter.ExcludeChips = ExcludeChips.Select(c => c.Keyword).ToList();
            _filter.ExcludeKeyword = _excludeKeyword;
            _filter.ExcludedSources = _excludedSources;

            FilteredView.Refresh();
            OnPropertyChanged(nameof(FilteredCount));
            OnPropertyChanged(nameof(IsEmpty));
            UpdateStatus();
        }

        private bool FilterPredicate(object obj)
        {
            return obj is LogEntry entry && _filter.IsMatch(entry);
        }

        // ── Statistics ────────────────────────────────────────────────────
        private void RefreshStatistics()
        {
            TotalCount = _allEntries.Count;
            ErrorCount = _allEntries.Count(e => (e.LevelDisplay ?? string.Empty) == "ERROR");
            WarnCount = _allEntries.Count(e =>
            {
                var l = e.LevelDisplay ?? string.Empty;
                return l == "WARN" || l == "WARNING";
            });
        }

        private void UpdateStatus()
        {
            if (IsLoading) return;
            int total = _allEntries.Count;
            int filtered = FilteredCount;
            StatusText = total == 0
                ? "로그 파일을 불러오세요"
                : $"표시: {filtered:N0} / 전체: {total:N0}  |  ERROR {ErrorCount:N0}  WARN {WarnCount:N0}";
        }

        /// <summary>Loaded log entries (full set, not affected by the search dialog).</summary>
        public IEnumerable<LogEntry> GetAllEntries() => _allEntries;

        /// <summary>Whether the entry would appear in the main list under the current filter.</summary>
        public bool IsEntryVisibleInMainList(LogEntry entry) =>
            entry != null && _filter.IsMatch(entry);

        // ── Helpers ───────────────────────────────────────────────────────
        private static string EscapeCsv(string s) => (s ?? string.Empty).Replace("\"", "\"\"");

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
