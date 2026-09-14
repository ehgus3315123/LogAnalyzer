using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LogViewer
{
    public class SourceExcludeItem : INotifyPropertyChanged
    {
        private readonly Action _onChanged;
        private bool _isIncluded;

        public SourceExcludeItem(string source, int count, bool isIncluded, Action onChanged)
        {
            Source = source ?? string.Empty;
            Count = count;
            _isIncluded = isIncluded;
            _onChanged = onChanged;
        }

        public string Source { get; }

        public int Count { get; }

        public string DisplayText =>
            string.IsNullOrEmpty(Source) ? $"(없음) ({Count:N0})" : $"{Source} ({Count:N0})";

        /// <summary>체크되면 해당 소스를 목록에 표시.</summary>
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (_isIncluded == value)
                    return;

                _isIncluded = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
