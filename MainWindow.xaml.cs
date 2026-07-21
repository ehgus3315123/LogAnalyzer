using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LogAnalyzer
{
    public partial class MainWindow : Window
    {
        public static readonly RoutedUICommand OpenLogSearchCommand = new RoutedUICommand(
            "로그 검색",
            "OpenLogSearch",
            typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.F, ModifierKeys.Control | ModifierKeys.Shift) });

        private readonly MainWindowViewModel _vm;
        private SearchWindow _searchWindow;

        public MainWindow()
        {
            InitializeComponent();
            CommandBindings.Add(new CommandBinding(OpenLogSearchCommand, (_, __) => ShowSearchWindow()));

            _vm = new MainWindowViewModel();
            DataContext = _vm;

            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Title = $"Log Analyzer v{v.Major}.{v.Minor}";

            // Wire Ctrl+F to focus the exclude box
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                ClearDragState();
                return;
            }

            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            bool isValid = paths != null && paths.Length == 1 && MainWindowViewModel.IsAllowedFile(paths[0]);

            if (_vm.IsStartupScreenVisible)
            {
                _vm.IsDragOver = true;
                _vm.IsDragOverValid = isValid;
            }

            e.Effects = isValid ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void MainWindow_DragLeave(object sender, DragEventArgs e)
        {
            // DragLeave는 자식 요소 경계에서도 발생하므로
            // 커서가 실제로 Window 클라이언트 영역 밖으로 나간 경우에만 초기화한다.
            var pos = e.GetPosition(this);
            if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
            {
                ClearDragState();
            }
        }

        private void ClearDragState()
        {
            _vm.IsDragOver = false;
            _vm.IsDragOverValid = false;
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            ClearDragState();

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;

            if (_vm.IsStartupScreenVisible)
            {
                if (paths.Length > 1)
                {
                    MessageBox.Show(
                        "파일을 1개만 드래그하세요.\n시작 화면에서는 단일 파일 분석만 지원합니다.",
                        "파일 드롭 오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                _vm.DropSingleFileOnStartup(paths[0]);
            }
            else
            {
                _vm.AddFilePaths(paths);
            }
        }

        private void AnyLogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int n = GetSelectedEntries().Count;
            ExportSelectedCsvButton.IsEnabled = n > 0;
            ExportSelectedTxtButton.IsEnabled = n > 0;
        }

        private void ExportSelectedCsv_Click(object sender, RoutedEventArgs e)
        {
            var items = GetSelectedEntries();
            _vm.ExportSelectedCsv(items);
        }

        private void ExportSelectedTxt_Click(object sender, RoutedEventArgs e)
        {
            var items = GetSelectedEntries();
            _vm.ExportSelectedTxt(items);
        }

        private void OpenSearch_Click(object sender, RoutedEventArgs e) => ShowSearchWindow();

        private void ShowSearchWindow()
        {
            if (_searchWindow != null && _searchWindow.IsVisible)
            {
                if (_searchWindow.WindowState == WindowState.Minimized)
                    _searchWindow.WindowState = WindowState.Normal;
                _searchWindow.Activate();
                return;
            }

            var w = new SearchWindow(() => _vm.GetAllEntries(), entry =>
            {
                if (!_vm.IsEntryVisibleInMainList(entry))
                {
                    MessageBox.Show(
                        "이 항목은 현재 제외 조건 때문에 로그 목록에 표시되지 않습니다.\n제외 조건을 완화한 뒤 다시 이동할 수 있습니다.",
                        "로그 검색",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _vm.SelectedEntry = entry;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogListView.ScrollIntoView(entry);
                    LogListView.Focus();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            });
            w.Owner = this;
            w.Closed += (_, __) =>
            {
                if (ReferenceEquals(_searchWindow, w))
                    _searchWindow = null;
            };
            _searchWindow = w;
            w.Show();
        }

        private void FilterChipSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _vm.AddFilterChipCommand.Execute(null);
            e.Handled = true;
        }

        private void ExcludeChipSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _vm.AddExcludeChipCommand.Execute(null);
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                FilterChipSearchBox.Focus();
                FilterChipSearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            var entries = GetSelectedEntries();
            if (entries.Count == 0) return;
            var text = string.Join(Environment.NewLine, entries.Select(x =>
                string.IsNullOrWhiteSpace(x.CallStack)
                    ? $"{x.TimestampDisplay}\t{x.LevelDisplay}\t{x.Source}\t{x.Message}"
                    : $"{x.TimestampDisplay}\t{x.LevelDisplay}\t{x.Source}\t{x.Message}\t{x.CallStack}"));
            Clipboard.SetText(text);
        }

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            var entries = GetSelectedEntries();
            if (entries.Count == 0) return;
            var text = string.Join(Environment.NewLine, entries.Select(x => $"{x.TimestampDisplay}\t{x.Message}"));
            Clipboard.SetText(text);
        }

        private System.Collections.Generic.List<LogEntry> GetSelectedEntries()
        {
            var selected = new System.Collections.Generic.HashSet<LogEntry>();
            foreach (var item in LogListView.SelectedItems.Cast<LogEntry>())
            {
                selected.Add(item);
            }

            if (CompareFirstListView != null)
            {
                foreach (var item in CompareFirstListView.SelectedItems.Cast<LogEntry>())
                {
                    selected.Add(item);
                }
            }

            if (CompareSecondListView != null)
            {
                foreach (var item in CompareSecondListView.SelectedItems.Cast<LogEntry>())
                {
                    selected.Add(item);
                }
            }

            return selected.ToList();
        }
    }
}
