using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LogAnalyzer
{
    /// <summary>
    /// Supports bulk add with a single reset notification.
    /// </summary>
    public class RangeObservableCollection<T> : ObservableCollection<T>
    {
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null)
            {
                return;
            }

            CheckReentrancy();

            int addedCount = 0;
            foreach (var item in items)
            {
                Items.Add(item);
                addedCount++;
            }

            if (addedCount == 0)
            {
                return;
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
