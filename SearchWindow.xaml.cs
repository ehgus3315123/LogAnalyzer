using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace LogAnalyzer
{
    public partial class SearchWindow : Window
    {
        public SearchWindow(Func<IEnumerable<LogEntry>> getEntries, Action<LogEntry> navigateToEntry)
        {
            InitializeComponent();
            DataContext = new SearchViewModel(getEntries, navigateToEntry);
        }

        private void SearchWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        }

        private void QueryBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (DataContext is SearchViewModel vm)
                vm.RunSearch();
            e.Handled = true;
        }

        private void ResultsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is SearchViewModel vm && vm.SelectedResult != null &&
                vm.GoToCommand.CanExecute(null))
                vm.GoToCommand.Execute(null);
        }

        private void Close_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
