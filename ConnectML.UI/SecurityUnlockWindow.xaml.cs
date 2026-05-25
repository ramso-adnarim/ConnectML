using System;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;

namespace ConnectML.UI
{
    public partial class SecurityUnlockWindow : Window
    {
        private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

        public Task<bool> UnlockTask => _tcs.Task;

        public SecurityUnlockWindow()
        {
            InitializeComponent();
            TxtUsername.Focus();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password;

            if (username == "service" && password == "service")
            {
                _tcs.TrySetResult(true);
                Close();
            }
            else
            {
                TxtError.Text = "Credenciais inválidas. Tente novamente!";
                TxtError.Visibility = Visibility.Visible;
                
                // Limpa a senha para nova tentativa
                TxtPassword.Clear();
                TxtPassword.Focus();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(false);
            Close();
        }

        private void Txt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirm_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _tcs.TrySetResult(false); // Garante que a task se resolve mesmo se fechar a janela de outra forma (ex: Alt+F4)
            base.OnClosed(e);
        }
    }
}
