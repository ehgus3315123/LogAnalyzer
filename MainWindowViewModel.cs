using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
        // ponytail: ICollectionView.Filter는 UI 스레드에서 항목을 하나씩 동기 평가해 대용량에서 UI가 멈춤.
        // 백그라운드에서 병렬 필터링 후 결과 리스트를 통째로 교체한다.
        private IReadOnlyList<LogEntry> _filteredEntries = Array.Empty<LogEntry>();
        public IReadOnlyList<LogEntry> FilteredEntries
        {
            get => _filteredEntries;
            private set { _filteredEntries = value; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredCount)); OnPropertyChanged(nameof(IsEmpty)); }
        }
        private CancellationTokenSource _filterCts;
        private bool _isFiltering;
        private bool _isUpdatingList;
        private bool _suppressFilterApply;
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
        private LogFilter _filter = new LogFilter();

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
            OpenFileCommand = new RelayCommand(OpenFile);
            ClearFilesCommand = new RelayCommand(ClearFiles, () => _allEntries.Count > 0 && !_isLoading);
            ToggleDarkModeCommand = new RelayCommand(ToggleDarkMode);
            ResetToStartupCommand = new RelayCommand(_ => ResetToStartup());
            AddFilterChipCommand = new RelayCommand(AddFilterChip);
            AddExcludeChipCommand = new RelayCommand(AddExcludeChip);
            OpenRecentFileCommand = new RelayCommand(OpenRecentFile);
            ClearRecentFilesCommand = new RelayCommand(ClearRecentFiles);
            RefreshFilesCommand = new RelayCommand(RefreshLoadedFiles, () => LoadedFiles.Count > 0 && !_isLoading);

            FilterChips.CollectionChanged += (_, __) => { SaveFilterChips(); if (!_suppressFilterApply) ApplyFilter(); };
            ExcludeChips.CollectionChanged += (_, __) => { SaveExcludeChips(); if (!_suppressFilterApply) ApplyFilter(); };
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
            private set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEmpty));
                NotifyBusy();
            }
        }

        public int LoadProgress
        {
            get => _loadProgress;
            private set { _loadProgress = value; OnPropertyChanged(); NotifyBusy(); }
        }

        /// <summary>로드·필터·목록 갱신 등 긴 작업 중이면 true. ProgressWindow 표시용.</summary>
        public bool IsBusy => _isLoading || _isFiltering || _isUpdatingList;

        public string BusyMessage
        {
            get
            {
                if (_isLoading) return $"파일 로딩 중... {_loadProgress}%";
                if (_isUpdatingList) return "목록 갱신 중...";
                if (_isFiltering) return "필터 적용 중...";
                return string.Empty;
            }
        }

        /// <summary>0–100, 또는 미정 진행이면 -1.</summary>
        public int BusyProgress => _isLoading ? _loadProgress : -1;

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

        public int FilteredCount => _filteredEntries.Count;
        public bool IsEmpty => !_isLoading && !_isFiltering && FilteredCount == 0;
        public bool HasSourceFilters => SourceFilters.Count > 0;

        // ── Commands ──────────────────────────────────────────────────────
        public ICommand OpenFileCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand ToggleDarkModeCommand { get; }
        public ICommand ResetToStartupCommand { get; }
        public ICommand AddFilterChipCommand { get; }
        public ICommand AddExcludeChipCommand { get; }
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
            _suppressFilterApply = true;
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
                _suppressFilterApply = false;
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

        // groups: (소스, 개수) — 백그라운드에서 미리 집계해 넘긴다.
        private void RefreshSourceFilters(IEnumerable<KeyValuePair<string, int>> groups)
        {
            _suppressSourceFilterCallback = true;
            try
            {
                SourceFilters.Clear();
                foreach (var group in groups)
                {
                    bool included = !_excludedSources.Contains(group.Key);
                    SourceFilters.Add(new SourceExcludeItem(group.Key, group.Value, included, OnSourceInclusionChanged));
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

            loader.Progress += (s, e) => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                LoadProgress = e.Percent;
                StatusText = $"로딩 중: {Path.GetFileName(e.FileName)} ({e.Percent}%)";
            }));

            loader.Completed += (s, e) => Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
            {
                // ponytail: ClearFiles()가 _pendingLoads=0으로 리셋한 뒤 지연 도착한 콜백 무시
                if (!_activeLoaders.Contains(loader)) return;

                _activeLoaders.Remove(loader);
                if (_pendingLoads > 0) _pendingLoads--;

                if (e.Error != null)
                {
                    MessageBox.Show($"파일 로드 실패:\n{e.Error.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else if (e.Entries != null)
                {
                    LoadProgress = 100;
                    StatusText = $"로딩 마무리 중: {Path.GetFileName(e.FileName)} (목록 반영)";
                    _isUpdatingList = true;
                    NotifyBusy();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    _allEntries.AddRange(e.Entries);
                    _isUpdatingList = false;
                    NotifyBusy();
                    RecordRecentFile(e.FileName);
                }

                if (_pendingLoads == 0)
                {
                    IsLoading = false;
                    LoadProgress = 100;
                    RefreshStatsAsync();
                    ApplyFilter();
                }
            }));

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
            _statsVersion++;
            RefreshStatistics();
            RefreshSourceFilters(Array.Empty<KeyValuePair<string, int>>());
            _filterCts?.Cancel();
            _isFiltering = false;
            _isUpdatingList = false;
            FilteredEntries = Array.Empty<LogEntry>();
            StatusText = "로그 파일을 불러오세요";
            LoadProgress = 0;
            IsLoading = false;
            NotifyBusy();
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


        private void ToggleDarkMode()
        {
            IsDarkMode = !IsDarkMode;
            ThemeManager.Apply(IsDarkMode ? "Dark" : "Light");
        }

        // ── Filter ────────────────────────────────────────────────────────
        private async void ApplyFilter()
        {
            _filterCts?.Cancel();
            var cts = _filterCts = new CancellationTokenSource();

            // 스레드에서 안전하게 쓰도록 매번 새 필터/스냅샷 사용 (UI 스레드에서 호출됨)
            var filter = _filter = new LogFilter
            {
                IncludeChips = FilterChips.Select(c => c.Keyword).ToList(),
                SearchKeyword = _searchKeyword,
                ExcludeChips = ExcludeChips.Select(c => c.Keyword).ToList(),
                ExcludeKeyword = _excludeKeyword,
                ExcludedSources = new HashSet<string>(_excludedSources, StringComparer.OrdinalIgnoreCase),
            };

            _isFiltering = true;
            StatusText = "필터 적용 중...";
            NotifyBusy();
            await Dispatcher.Yield(DispatcherPriority.Background);

            if (cts.IsCancellationRequested) { FinishFilterBusy(); return; }

            var snapshot = _allEntries.ToArray();

            List<LogEntry> result;
            try
            {
                result = await Task.Run(() => snapshot.AsParallel().AsOrdered()
                    .WithCancellation(cts.Token).Where(filter.IsMatch).ToList(), cts.Token);
            }
            catch (OperationCanceledException) { FinishFilterBusy(); return; }

            if (cts.IsCancellationRequested) { FinishFilterBusy(); return; }

            _isFiltering = false;
            _isUpdatingList = true;
            NotifyBusy();
            await Dispatcher.Yield(DispatcherPriority.Background);
            FilteredEntries = result;
            _isUpdatingList = false;
            NotifyBusy();
            UpdateStatus();
        }

        private void FinishFilterBusy()
        {
            // 더 새 필터가 돌고 있으면 그쪽이 busy를 유지한다
            if (_filterCts != null && !_filterCts.IsCancellationRequested) return;
            _isFiltering = false;
            _isUpdatingList = false;
            NotifyBusy();
        }

        private void NotifyBusy()
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(BusyMessage));
            OnPropertyChanged(nameof(BusyProgress));
        }

        private static Dispatcher Dispatcher =>
            Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

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

        private int _statsVersion;

        // 로드 완료 후 레벨 카운트·소스 집계를 백그라운드에서 수행 (UI 스레드 점유 방지)
        private async void RefreshStatsAsync()
        {
            int version = ++_statsVersion;
            var snapshot = _allEntries.ToArray();

            var r = await Task.Run(() =>
            {
                int err = 0, warn = 0;
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in snapshot)
                {
                    var l = e.LevelDisplay;
                    if (l == "ERROR") err++;
                    else if (l == "WARN" || l == "WARNING") warn++;
                    var src = e.Source ?? string.Empty;
                    counts.TryGetValue(src, out int c);
                    counts[src] = c + 1;
                }
                var groups = counts.OrderByDescending(kv => kv.Value)
                                   .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
                return (err, warn, groups);
            });

            if (version != _statsVersion) return; // 그 사이 파일이 바뀜
            ErrorCount = r.err;
            WarnCount = r.warn;
            RefreshSourceFilters(r.groups);
            TotalCount = snapshot.Length; // UpdateStatus 포함
        }

        private void UpdateStatus()
        {
            if (IsLoading || _isFiltering) return;
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

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
