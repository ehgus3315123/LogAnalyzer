using System.ComponentModel;
using System.Windows;

namespace LogViewer
{
    public partial class ProgressWindow : Window
    {
        private bool _allowClose;

        public ProgressWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string message, int? percent)
        {
            MessageText.Text = string.IsNullOrEmpty(message) ? "작업 중..." : message;
            if (percent.HasValue)
            {
                Bar.IsIndeterminate = false;
                Bar.Value = percent.Value;
                PercentText.Text = percent.Value + "%";
                PercentText.Visibility = Visibility.Visible;
            }
            else
            {
                Bar.IsIndeterminate = true;
                PercentText.Visibility = Visibility.Collapsed;
            }
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // ponytail: 작업 중 Alt+F4로 닫히면 상태만 꼬임 — 강제 종료만 허용
            if (!_allowClose) e.Cancel = true;
        }
    }
}
