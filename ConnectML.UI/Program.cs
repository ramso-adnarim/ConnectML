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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        [STAThread]
        public static void Main(string[] args)
        {
            // 1. Verificação de Instância Única (Single Instance) via Mutex antes de qualquer outra lógica
            bool createdNew;
            _mutex = new Mutex(true, "Global\\ConnectML_App_Mutex", out createdNew);

            if (!createdNew)
            {
                // Instância anterior já está ativa. Procura a janela existente para restaurá-la.
                IntPtr hWnd = FindWindow(null, "ConnectML - V1.1.0");
                if (hWnd != IntPtr.Zero)
                {
                    // Restaura a janela se estiver minimizada
                    ShowWindow(hWnd, SW_RESTORE);
                    // Traz a janela para primeiro plano
                    SetForegroundWindow(hWnd);
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
