using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ConnectML.Infrastructure.Interop
{
    /// <summary>
    /// Utilitário Win32 para localização de janelas ativas por título e transferência de foco de teclado.
    /// Utilizado para posicionar o software MeasurLink em primeiro plano antes da simulação de atalhos.
    /// </summary>
    public static class WindowFocusHelper
    {
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_MENU = 0x12; // Alt key

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        /// <summary>
        /// Localiza a primeira janela visível cujo título contenha o texto informado (case-insensitive).
        /// </summary>
        public static IntPtr FindWindowByPartialTitle(string partialTitle)
        {
            if (string.IsNullOrWhiteSpace(partialTitle))
                return IntPtr.Zero;

            IntPtr foundHwnd = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true; // Continua a busca

                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                var builder = new StringBuilder(length + 1);
                GetWindowText(hWnd, builder, builder.Capacity);
                string title = builder.ToString();

                if (title.IndexOf(partialTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foundHwnd = hWnd;
                    return false; // Interrompe enumeração
                }

                return true;
            }, IntPtr.Zero);

            return foundHwnd;
        }

        /// <summary>
        /// Traz a janela especificada para o primeiro plano, restaurando-a se estiver minimizada
        /// e garantindo a conexão de entrada de teclado.
        /// </summary>
        public static bool BringWindowToForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return false;

            try
            {
                // Se a janela estiver minimizada, restaura
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                else
                {
                    ShowWindow(hWnd, SW_SHOW);
                }

                uint foregroundThreadId = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                uint currentThreadId = GetCurrentThreadId();
                uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

                // Libera restrição do Windows simulando toque rápido na tecla Alt
                keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                bool attached = false;
                if (targetThreadId != currentThreadId)
                {
                    attached = AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                bool result = SetForegroundWindow(hWnd);

                if (attached)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Interop] Erro ao trazer janela para primeiro plano");
                return false;
            }
        }

        /// <summary>
        /// Localiza uma janela pelo título parcial, restaura e posiciona em foco.
        /// </summary>
        public static async Task<bool> FindAndFocusWindowAsync(string partialTitle, int preDelayMs = 80)
        {
            IntPtr hWnd = FindWindowByPartialTitle(partialTitle);
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            bool focused = BringWindowToForeground(hWnd);
            if (focused && preDelayMs > 0)
            {
                await Task.Delay(preDelayMs);
            }

            return focused;
        }
    }
}
