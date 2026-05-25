using System;
using System.Runtime.InteropServices;
using System.Threading;
using Velopack;

namespace ConnectML.UI
{
    public class Program
    {
        private static Mutex? _mutex;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        public static void Main(string[] args)
        {
            // 1. Verificação de Instância Única (Single Instance) via Mutex antes de qualquer outra lógica
            bool createdNew;
            _mutex = new Mutex(true, "Global\\ConnectML_App_Mutex", out createdNew);

            if (!createdNew)
            {
                // Instância anterior já está ativa. Procura a janela existente.
                IntPtr hWnd = FindWindow(null, "ConnectML - V1.1.0");
                if (hWnd != IntPtr.Zero)
                {
                    // Envia uma mensagem customizada para que a instância ativa se restaure de forma limpa pelo thread do WPF
                    int wmShowMe = RegisterWindowMessage("WM_SHOWME_CONNECTML");
                    PostMessage(hWnd, wmShowMe, IntPtr.Zero, IntPtr.Zero);
                }

                // Encerra a execução desta nova instância silenciosamente
                Environment.Exit(0);
                return;
            }

            try
            {
                // O VelopackApp DEVE ser o primeiro código a rodar na aplicação!
                VelopackApp.Build().Run();

                // Inicializa a aplicação WPF manualmente
                var app = new ConnectML.UI.App();
                app.InitializeComponent();
                app.Run();
            }
            finally
            {
                // Libera o mutex adequadamente ao fechar a aplicação
                if (_mutex != null)
                {
                    try
                    {
                        _mutex.ReleaseMutex();
                    }
                    catch (ObjectDisposedException) { }
                    _mutex.Dispose();
                }
            }
        }
    }
}
