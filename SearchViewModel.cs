using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LogViewer
{
    public class SearchViewModel : INotifyPropertyChanged
    {
        private readonly Func<IEnumerable<LogEntry>> _getEntries;
        private readonly Action<LogEntry> _navigate;
        private string _query = string.Empty;
        private LogEntry _selectedResult;
        private bool _isSearching;

        public SearchViewModel(Func<IEnumerable<LogEntry>> getEntries, Action<LogEntry> navigate)
        {
            _getEntries = getEntries ?? throw new ArgumentNullException(nameof(getEntries));
            _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));

            SearchCommand = new RelayCommand(RunSearch);
            GoToCommand = new RelayCommand(() => NavigateToSelected(), () => SelectedResult != null);
            ApplyRecentSearchCommand = new RelayCommand(o => ApplyRecentSearch(o as RecentSearchItem));
            RemoveRecentSearchCommand = new RelayCommand(o => RemoveRecentSearch(o as RecentSearchItem));
            ClearRecentSearchesCommand = new RelayCommand(ClearRecentSearches);

            LoadRecentSearches();
        }

        public string Query
        {
            get => _query;
            set
            {
                if (_query == value) return;
                _query = value;
                OnPropertyChanged();
            }
        }

        public LogEntry SelectedResult
        {
            get => _selectedResult;
            set
            {
                if (_selectedResult == value) return;
                _selectedResult = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public RangeObservableCollection<LogEntry> Results { get; } = new RangeObservableCollection<LogEntry>();

        public string ResultSummary
        {
            get
            {
                int n = Results.Count;
                return n == 0 ? "검색 결과 없음" : $"검색 결과 {n:N0}건";
            }
        }

        public ObservableCollection<RecentSearchItem> RecentSearches { get; } = new ObservableCollection<RecentSearchItem>();

        public bool HasRecentSearches => RecentSearches.Count > 0;

        public bool IsBusy => _isSearching;
        public string BusyMessage => _isSearching ? "검색 중..." : string.Empty;
        public int BusyProgress => -1;

        public ICommand SearchCommand { get; }
        public ICommand GoToCommand { get; }
        public ICommand ApplyRecentSearchCommand { get; }
        public ICommand RemoveRecentSearchCommand { get; }
        public ICommand ClearRecentSearchesCommand { get; }

        private int _searchVersion;

        public async void RunSearch()
        {
            int version = ++_searchVersion;
            Results.Clear();
            SelectedResult = null;
            OnPropertyChanged(nameof(ResultSummary));

            string query = Query;
            if (string.IsNullOrWhiteSpace(query))
                return;

            _isSearching = true;
            NotifyBusy();

            LogEntry[] snapshot; // UI 스레드에서 스냅샷 → 백그라운드에서 안전하게 순회
            try
            {
                snapshot = (_getEntries() ?? Enumerable.Empty<LogEntry>()).ToArray();
            }
            catch
            {
                if (version == _searchVersion) { _isSearching = false; NotifyBusy(); }
                return;
            }

            var found = await Task.Run(() => snapshot.AsParallel().AsOrdered()
                .Where(e => e != null && (e.RawLine ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList());

            if (version != _searchVersion) return; // 더 새로운 검색이 시작됨
            Results.AddRange(found);
            OnPropertyChanged(nameof(ResultSummary));
            _isSearching = false;
            NotifyBusy();

            RecentSearchesService.Add(Query);
            LoadRecentSearches();
        }

        private void NotifyBusy()
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(BusyMessage));
            OnPropertyChanged(nameof(BusyProgress));
        }

        private void LoadRecentSearches()
        {
            RecentSearches.Clear();
            foreach (var item in RecentSearchesService.Load())
                RecentSearches.Add(item);
            OnPropertyChanged(nameof(HasRecentSearches));
        }

        public Action FocusQueryBox { get; set; }

        private void ApplyRecentSearch(RecentSearchItem item)
        {
            if (item == null) return;
            Query = item.Text;
            FocusQueryBox?.Invoke();
        }

        private void RemoveRecentSearch(RecentSearchItem item)
        {
            if (item == null) return;
            RecentSearchesService.Remove(item.Text);
            LoadRecentSearches();
        }

        private void ClearRecentSearches()
        {
            RecentSearchesService.Clear();
            LoadRecentSearches();
        }

        private void NavigateToSelected()
        {
            if (SelectedResult != null)
                _navigate(SelectedResult);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
