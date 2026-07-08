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

namespace LogAnalyzer
{
    public enum AnalysisMode
    {
        SingleFile,
        MultiFileCompare
    }

    public class FilterChip
    {
        public string Keyword { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // ── Backing fields ────────────────────────────────────────────────
        private string _searchKeyword = string.Empty;
        private bool _isLoading;
        private int _loadProgress;
        private string _statusText = "분석 모드를 선택하세요";
        private bool _isDarkMode;
        private LogEntry _selectedEntry;
        private AnalysisMode? _selectedMode;
        private bool _isStartupScreenVisible = true;
        private string _compareFirstFilePath = string.Empty;
        private string _compareSecondFilePath = string.Empty;
        private bool _isDragOver;
        private bool _isDragOverValid;

        // ── Collections ──────────────────────────────────────────────────
        private readonly RangeObservableCollection<LogEntry> _allEntries = new RangeObservableCollection<LogEntry>();
        private readonly ObservableCollection<LogEntry> _compareFirstEntries = new ObservableCollection<LogEntry>();
        private readonly ObservableCollection<LogEntry> _compareSecondEntries = new ObservableCollection<LogEntry>();
        public ListCollectionView FilteredView { get; }
        public ListCollectionView CompareFirstView { get; }
        public ListCollectionView CompareSecondView { get; }
        public ObservableCollection<string> LoadedFiles { get; } = new ObservableCollection<string>();
        public ObservableCollection<FilterChip> FilterChips { get; } = new ObservableCollection<FilterChip>();

        // ── Statistics (카운트만 유지 — 하단 바 표시용) ──────────────────
        private int _totalCount, _errorCount, _warnCount;

        // ── Loaders in progress ───────────────────────────────────────────
        private readonly List<LogLoader> _activeLoaders = new List<LogLoader>();
        private int _pendingLoads;

        // ── Filter object (stateless snapshot, rebuilt on change) ─────────
        private readonly LogFilter _filter = new LogFilter();

        public MainWindowViewModel()
        {
            FilteredView = new ListCollectionView(_allEntries);
            FilteredView.Filter = FilterPredicate;
            CompareFirstView = new ListCollectionView(_compareFirstEntries);
            CompareSecondView = new ListCollectionView(_compareSecondEntries);

            AddFilesCommand = new RelayCommand(AddFiles);
            ClearFilesCommand = new RelayCommand(ClearFiles, () => _allEntries.Count > 0 && !_isLoading);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => FilteredCount > 0);
            ExportTxtCommand = new RelayCommand(ExportTxt, () => FilteredCount > 0);
            ToggleDarkModeCommand = new RelayCommand(ToggleDarkMode);
            SelectSingleModeCommand = new RelayCommand(SelectSingleMode);
            SelectCompareModeCommand = new RelayCommand(SelectCompareMode);
            BrowseCompareFirstFileCommand = new RelayCommand(BrowseCompareFirstFile);
            BrowseCompareSecondFileCommand = new RelayCommand(BrowseCompareSecondFile);
            StartCompareModeCommand = new RelayCommand(StartCompareMode, CanStartCompareMode);
            ResetToStartupCommand = new RelayCommand(_ => ResetToStartup());
            AddFilterChipCommand = new RelayCommand(AddFilterChip);

            FilterChips.CollectionChanged += (_, __) => ApplyFilter();
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
                // 칩 없을 때 실시간 필터
                if (FilterChips.Count == 0) ApplyFilter();
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

        public AnalysisMode? SelectedMode
        {
            get => _selectedMode;
            private set
            {
                _selectedMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsModeSelected));
                OnPropertyChanged(nameof(IsSingleMode));
                OnPropertyChanged(nameof(IsCompareMode));
                OnPropertyChanged(nameof(IsCompareSetupVisible));
            }
        }

        public bool IsModeSelected => SelectedMode.HasValue;
        public bool IsSingleMode => SelectedMode == AnalysisMode.SingleFile;
        public bool IsCompareMode => SelectedMode == AnalysisMode.MultiFileCompare;

        public bool IsStartupScreenVisible
        {
            get => _isStartupScreenVisible;
            private set
            {
                _isStartupScreenVisible = value;
                OnPropertyChanged();
            }
        }

        public bool IsCompareSetupVisible => IsStartupScreenVisible && IsCompareMode;

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

        public string CompareFirstFilePath
        {
            get => _compareFirstFilePath;
            set
            {
                if (_compareFirstFilePath == value) return;
                _compareFirstFilePath = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompareFirstFileName));
                UpdateCompareViews();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string CompareSecondFilePath
        {
            get => _compareSecondFilePath;
            set
            {
                if (_compareSecondFilePath == value) return;
                _compareSecondFilePath = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompareSecondFileName));
                UpdateCompareViews();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string CompareFirstFileName => string.IsNullOrWhiteSpace(CompareFirstFilePath)
            ? "(첫 번째 파일)"
            : Path.GetFileName(CompareFirstFilePath);

        public string CompareSecondFileName => string.IsNullOrWhiteSpace(CompareSecondFilePath)
            ? "(두 번째 파일)"
            : Path.GetFileName(CompareSecondFilePath);

        public int TotalCount { get => _totalCount; private set { _totalCount = value; OnPropertyChanged(); UpdateStatus(); } }
        public int ErrorCount { get => _errorCount; private set { _errorCount = value; OnPropertyChanged(); } }
        public int WarnCount { get => _warnCount; private set { _warnCount = value; OnPropertyChanged(); } }

        public int FilteredCount => FilteredView?.Count ?? 0;
        public int CompareFirstCount => CompareFirstView?.Count ?? 0;
        public int CompareSecondCount => CompareSecondView?.Count ?? 0;
        public bool IsEmpty => !_isLoading && FilteredCount == 0;

        // ── Commands ──────────────────────────────────────────────────────
        public ICommand AddFilesCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportTxtCommand { get; }
        public ICommand ToggleDarkModeCommand { get; }
        public ICommand SelectSingleModeCommand { get; }
        public ICommand SelectCompareModeCommand { get; }
        public ICommand BrowseCompareFirstFileCommand { get; }
        public ICommand BrowseCompareSecondFileCommand { get; }
        public ICommand StartCompareModeCommand { get; }
        public ICommand ResetToStartupCommand { get; }
        public ICommand AddFilterChipCommand { get; }

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

        // ── Command Implementations ───────────────────────────────────────
        private void AddFiles(object _)
        {
            if (!IsModeSelected)
            {
                MessageBox.Show("먼저 시작 화면에서 분석 모드를 선택하세요.", "모드 선택", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "로그 파일 선택",
                Filter = "로그 파일 (*.log;*.txt)|*.log;*.txt|모든 파일 (*.*)|*.*",
                Multiselect = IsCompareMode
            };
            if (dlg.ShowDialog() != true) return;
            if (IsSingleMode)
            {
                AddFilePaths(new[] { dlg.FileName });
                return;
            }

            AddFilePaths(dlg.FileNames);
        }

        private static readonly HashSet<string> ALLOWED_EXTENSIONS =
            new HashSet<string>(new[] { ".log", ".txt" }, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 시작 화면에서 단일 파일 드래그 앤 드롭 처리.
        /// 유효한 파일이면 단일 파일 분석을 바로 시작한다.
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

            SelectedMode = AnalysisMode.SingleFile;
            CompleteStartup();
            ClearFiles();
            LoadedFiles.Add(path);
            LoadFile(path);
        }

        /// <summary>
        /// 드래그 대상 파일이 허용된 확장자인지 검사한다.
        /// </summary>
        public static bool IsAllowedFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return ALLOWED_EXTENSIONS.Contains(Path.GetExtension(path));
        }

        /// <summary>여러 경로를 한 번에 추가합니다(파일 대화상자, 드래그 앤 드롭 등).</summary>
        public void AddFilePaths(IEnumerable<string> paths)
        {
            if (paths == null) return;
            if (!IsModeSelected)
            {
                MessageBox.Show("먼저 시작 화면에서 분석 모드를 선택하세요.", "모드 선택", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var validPaths = new List<string>();
            foreach (var raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    if (!File.Exists(raw)) continue;
                    var path = Path.GetFullPath(raw);
                    if (!validPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        validPaths.Add(path);
                    }
                }
                catch
                {
                    // Ignore invalid paths from drag/drop or dialog.
                }
            }

            if (validPaths.Count == 0) return;

            if (IsSingleMode)
            {
                var singlePath = validPaths[0];
                ClearFiles();
                LoadedFiles.Add(singlePath);
                LoadFile(singlePath);
                return;
            }

            foreach (var path in validPaths)
            {
                if (LoadedFiles.Contains(path)) continue;
                LoadedFiles.Add(path);
                LoadFile(path);
            }

            UpdateCompareTargetsFromLoadedFiles();
            UpdateCompareViews();
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
                }

                if (_pendingLoads == 0)
                {
                    IsLoading = false;
                    LoadProgress = 100;
                    RefreshStatistics();
                    FilteredView.Refresh();
                    UpdateCompareViews();
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
            FilteredView.Refresh();
            UpdateCompareViews();
            OnPropertyChanged(nameof(FilteredCount));
            StatusText = IsModeSelected ? "로그 파일을 불러오세요" : "분석 모드를 선택하세요";
            LoadProgress = 0;
            IsLoading = false;
        }

        private void ResetToStartup()
        {
            ClearFiles();
            SelectedMode = null;
            CompareFirstFilePath = string.Empty;
            CompareSecondFilePath = string.Empty;
            IsStartupScreenVisible = true;
            FilterChips.Clear();
            SearchKeyword = string.Empty;
        }

        private void SelectSingleMode()
        {
            SelectedMode = AnalysisMode.SingleFile;
            UpdateCompareSetupVisibility();

            var dlg = new OpenFileDialog
            {
                Title = "단일 분석 파일 선택",
                Filter = "로그 파일 (*.log;*.txt)|*.log;*.txt|모든 파일 (*.*)|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true)
            {
                SelectedMode = null;
                UpdateCompareSetupVisibility();
                UpdateStatus();
                return;
            }

            CompleteStartup();
            AddFilePaths(new[] { dlg.FileName });
        }

        private void SelectCompareMode()
        {
            SelectedMode = AnalysisMode.MultiFileCompare;
            UpdateCompareSetupVisibility();
            UpdateCompareViews();
            UpdateStatus();
        }

        private void BrowseCompareFirstFile()
        {
            var selectedPath = SelectCompareFile("첫 번째 파일 선택");
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                CompareFirstFilePath = selectedPath;
            }
        }

        private void BrowseCompareSecondFile()
        {
            var selectedPath = SelectCompareFile("두 번째 파일 선택");
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                CompareSecondFilePath = selectedPath;
            }
        }

        private bool CanStartCompareMode(object _)
        {
            return IsCompareMode
                && File.Exists(CompareFirstFilePath)
                && File.Exists(CompareSecondFilePath)
                && !string.Equals(CompareFirstFilePath, CompareSecondFilePath, StringComparison.OrdinalIgnoreCase);
        }

        private void StartCompareMode(object _)
        {
            if (!CanStartCompareMode(null))
            {
                MessageBox.Show("서로 다른 두 개의 유효한 파일을 선택하세요.", "비교 모드", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CompleteStartup();
            ClearFiles();
            AddFilePaths(new[] { CompareFirstFilePath, CompareSecondFilePath });
        }

        private string SelectCompareFile(string title)
        {
            var dlg = new OpenFileDialog
            {
                Title = title,
                Filter = "로그 파일 (*.log;*.txt)|*.log;*.txt|모든 파일 (*.*)|*.*",
                Multiselect = false
            };

            return dlg.ShowDialog() == true ? dlg.FileName : string.Empty;
        }

        private void CompleteStartup()
        {
            IsStartupScreenVisible = false;
            UpdateCompareSetupVisibility();
        }

        private void UpdateCompareSetupVisibility()
        {
            OnPropertyChanged(nameof(IsCompareSetupVisible));
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
                var lines = FilteredView.Cast<LogEntry>().Select(e => e.RawLine);
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
                var lines = ordered.Select(e => e.RawLine);
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
            sb.AppendLine("LineNumber,Timestamp,Level,Source,Message,FileName");
            foreach (var e in entries)
            {
                sb.AppendLine($"{e.LineNumber}," +
                              $"\"{e.TimestampDisplay}\"," +
                              $"{e.LevelDisplay}," +
                              $"\"{e.Source}\"," +
                              $"\"{EscapeCsv(e.Message)}\"," +
                              $"\"{e.FileName}\"");
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
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

            FilteredView.Refresh();
            UpdateCompareViews();
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
                ? (IsModeSelected ? "로그 파일을 불러오세요" : "분석 모드를 선택하세요")
                : $"표시: {filtered:N0} / 전체: {total:N0}  |  ERROR {ErrorCount:N0}  WARN {WarnCount:N0}";
        }

        /// <summary>Loaded log entries (full set, not affected by the search dialog).</summary>
        public IEnumerable<LogEntry> GetAllEntries() => _allEntries;

        /// <summary>Whether the entry would appear in the main list under the current filter.</summary>
        public bool IsEntryVisibleInMainList(LogEntry entry) =>
            entry != null && _filter.IsMatch(entry);

        private void UpdateCompareTargetsFromLoadedFiles()
        {
            if (LoadedFiles.Count > 0 && string.IsNullOrWhiteSpace(CompareFirstFilePath))
            {
                CompareFirstFilePath = LoadedFiles[0];
            }

            if (LoadedFiles.Count > 1 && string.IsNullOrWhiteSpace(CompareSecondFilePath))
            {
                CompareSecondFilePath = LoadedFiles[1];
            }
        }

        private void UpdateCompareViews()
        {
            if (!IsCompareMode)
            {
                ReplaceEntries(_compareFirstEntries, Array.Empty<LogEntry>());
                ReplaceEntries(_compareSecondEntries, Array.Empty<LogEntry>());
                OnPropertyChanged(nameof(CompareFirstCount));
                OnPropertyChanged(nameof(CompareSecondCount));
                return;
            }

            var firstName = string.IsNullOrWhiteSpace(CompareFirstFilePath) ? string.Empty : Path.GetFileName(CompareFirstFilePath);
            var secondName = string.IsNullOrWhiteSpace(CompareSecondFilePath) ? string.Empty : Path.GetFileName(CompareSecondFilePath);
            var visibleEntries = FilteredView.Cast<LogEntry>().ToList();

            ReplaceEntries(_compareFirstEntries, visibleEntries.Where(entry =>
                !string.IsNullOrWhiteSpace(firstName)
                && string.Equals(entry.FileName, firstName, StringComparison.OrdinalIgnoreCase)));
            ReplaceEntries(_compareSecondEntries, visibleEntries.Where(entry =>
                !string.IsNullOrWhiteSpace(secondName)
                && string.Equals(entry.FileName, secondName, StringComparison.OrdinalIgnoreCase)));

            CompareFirstView.Refresh();
            CompareSecondView.Refresh();
            OnPropertyChanged(nameof(CompareFirstCount));
            OnPropertyChanged(nameof(CompareSecondCount));
        }

        private static void ReplaceEntries(ObservableCollection<LogEntry> target, IEnumerable<LogEntry> entries)
        {
            target.Clear();
            foreach (var entry in entries)
            {
                target.Add(entry);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static string EscapeCsv(string s) => (s ?? string.Empty).Replace("\"", "\"\"");

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
