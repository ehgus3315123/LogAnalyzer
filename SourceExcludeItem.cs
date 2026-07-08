using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LogAnalyzer
{
    public class SourceExcludeItem : INotifyPropertyChanged
    {
        private readonly Action<bool> _onExcludedChanged;
        private bool _isExcluded;

        public SourceExcludeItem(string source, int count, bool isExcluded, Action<bool> onExcludedChanged)
        {
            Source = source ?? string.Empty;
            Count = count;
            _isExcluded = isExcluded;
            _onExcludedChanged = onExcludedChanged;
        }

        public string Source { get; }

        public int Count { get; }

        public bool IsExcluded
        {
            get => _isExcluded;
            set
            {
                if (_isExcluded == value)
                {
                    return;
                }

                _isExcluded = value;
                OnPropertyChanged();
                _onExcludedChanged?.Invoke(value);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
