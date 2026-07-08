using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace LogAnalyzer
{
    public class SearchViewModel : INotifyPropertyChanged
    {
        private readonly Func<IEnumerable<LogEntry>> _getEntries;
        private readonly Action<LogEntry> _navigate;
        private string _query = string.Empty;
        private bool _useRegex;
        private string _regexError = string.Empty;
        private LogEntry _selectedResult;

        public SearchViewModel(Func<IEnumerable<LogEntry>> getEntries, Action<LogEntry> navigate)
        {
            _getEntries = getEntries ?? throw new ArgumentNullException(nameof(getEntries));
            _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));

            SearchCommand = new RelayCommand(RunSearch);
            GoToCommand = new RelayCommand(() => NavigateToSelected(), () => SelectedResult != null);
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

        public bool UseRegex
        {
            get => _useRegex;
            set
            {
                if (_useRegex == value) return;
                _useRegex = value;
                OnPropertyChanged();
                RegexError = string.Empty;
            }
        }

        public string RegexError
        {
            get => _regexError;
            private set { _regexError = value; OnPropertyChanged(); }
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

        public ObservableCollection<LogEntry> Results { get; } = new ObservableCollection<LogEntry>();

        public string ResultSummary
        {
            get
            {
                int n = Results.Count;
                return n == 0 ? "검색 결과 없음" : $"검색 결과 {n:N0}건";
            }
        }

        public ICommand SearchCommand { get; }
        public ICommand GoToCommand { get; }

        public void RunSearch()
        {
            Results.Clear();
            SelectedResult = null;
            RegexError = string.Empty;
            OnPropertyChanged(nameof(ResultSummary));

            if (string.IsNullOrWhiteSpace(Query))
                return;

            IEnumerable<LogEntry> source;
            try
            {
                source = _getEntries() ?? Enumerable.Empty<LogEntry>();
            }
            catch
            {
                return;
            }

            if (UseRegex)
            {
                Regex rx;
                try
                {
                    rx = new Regex(Query, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                catch (ArgumentException)
                {
                    RegexError = "잘못된 정규식";
                    return;
                }

                foreach (var e in source)
                {
                    if (e != null && rx.IsMatch(e.RawLine ?? string.Empty))
                        Results.Add(e);
                }
            }
            else
            {
                foreach (var e in source)
                {
                    if (e == null) continue;
                    if ((e.RawLine ?? string.Empty).IndexOf(Query, StringComparison.OrdinalIgnoreCase) >= 0)
                        Results.Add(e);
                }
            }

            OnPropertyChanged(nameof(ResultSummary));
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
