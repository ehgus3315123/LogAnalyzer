using System.Windows;
using System.Windows.Threading;

namespace LogViewer
{
    /// <summary>
    /// Owner 창 위에 ProgressWindow를 띄워 busy 상태를 표시한다.
    /// Owner를 Disable하지 않는다(소유 창도 함께 비활성화되는 WPF 동작 회피).
    /// </summary>
    public sealed class ProgressHost
    {
        private readonly Window _owner;
        private ProgressWindow _window;

        public ProgressHost(Window owner)
        {
            _owner = owner;
        }

        public void Sync(bool isBusy, string message, int progress)
        {
            if (!isBusy)
            {
                if (_window != null)
                {
                    _window.ForceClose();
                    _window = null;
                }
                return;
            }

            if (_window == null)
            {
                _window = new ProgressWindow { Owner = _owner };
                _window.SetStatus(message, progress < 0 ? null : (int?)progress);
                _window.Show();
                // 방금 띄운 창이 그려진 뒤 무거운 UI 작업이 이어지도록 한 프레임 양보
                _owner.Dispatcher.Invoke(DispatcherPriority.Render, new System.Action(() => { }));
            }
            else
            {
                _window.SetStatus(message, progress < 0 ? null : (int?)progress);
            }
        }
    }
}
