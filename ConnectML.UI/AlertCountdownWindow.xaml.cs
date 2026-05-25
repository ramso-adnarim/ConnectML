using System;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace ConnectML.UI
{
    public partial class AlertCountdownWindow : Window
    {
        private int _secondsRemaining = 6;
        private DispatcherTimer? _timer;
        private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

        public Task<bool> CountdownTask => _tcs.Task;

        public AlertCountdownWindow()
        {
            InitializeComponent();
            
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            UpdateText();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _secondsRemaining--;
            UpdateText();

            if (_secondsRemaining <= 0)
            {
                _timer?.Stop();
                _timer = null;
                _tcs.TrySetResult(true);
                Close();
            }
        }

        private void UpdateText()
        {
            TxtCountdown.Text = $"Tentando novamente em {_secondsRemaining}s...";
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _timer = null;
            _tcs.TrySetResult(false);
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            _timer = null;
            _tcs.TrySetResult(false); // Garante que a task se resolve mesmo se fechar a janela de outra forma
            base.OnClosed(e);
        }
    }
}
