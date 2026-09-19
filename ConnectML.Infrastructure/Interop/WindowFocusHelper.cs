using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ConnectML.Infrastructure.Interop
{
    /// <summary>
    /// Utilitário Win32 para localização de janelas ativas com algoritmo de pontuação e eliminação
    /// rigorosa de falsos positivos (como o Windows Explorer), além de transferência segura de foco.
    /// Utilizado para posicionar o software MeasurLink em primeiro plano antes da simulação de atalhos.
    /// </summary>
    public static class WindowFocusHelper
    {
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_MENU = 0x12; // Alt
        private const uint LSFW_UNLOCK = 2;
        private const uint GW_OWNER = 4;
        private const uint MAPVK_VK_TO_VSC = 0;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

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
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool LockSetForegroundWindow(uint uLockCode);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        /// <summary>
        /// Localiza a janela do software alvo com algoritmo de pontuação e eliminação rigorosa
        /// de falsos positivos (como pastas do Windows Explorer abertas com nome do software).
        /// </summary>
        public static IntPtr FindTargetWindow(string targetHint)
        {
            if (string.IsNullOrWhiteSpace(targetHint))
                return IntPtr.Zero;

            bool isMeasurLinkTarget = targetHint.IndexOf("MeasurLink", StringComparison.OrdinalIgnoreCase) >= 0;

            IntPtr bestHwnd = IntPtr.Zero;
            int bestScore = int.MinValue;
            string bestTitle = string.Empty;
            string bestProc = string.Empty;
            string bestClass = string.Empty;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindow(hWnd))
                    return true;

                // Título da janela
                int length = GetWindowTextLength(hWnd);
                var sbTitle = new StringBuilder(length + 1);
                if (length > 0)
                {
                    GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
                }
                string title = sbTitle.ToString();

                // Classe da janela
                var sbClass = new StringBuilder(256);
                GetClassName(hWnd, sbClass, 256);
                string className = sbClass.ToString();

                // Processo proprietário
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0)
                    return true;

                string procName = string.Empty;
                string procPath = string.Empty;
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                    try { procPath = proc.MainModule?.FileName ?? string.Empty; } catch { }
                }
                catch
                {
                    return true;
                }

                // ====================================================================
                // 1. FILTROS RÍGIDOS DE EXCLUSÃO (Falsos Positivos Conhecidos)
                // ====================================================================

                // 1.1 Windows Explorer (Pastas abertas com nome 'MeasurLink' NUNCA devem ser focadas)
                if (string.Equals(procName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                    className.StartsWith("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                    className.StartsWith("ExploreWClass", StringComparison.OrdinalIgnoreCase) ||
                    className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd")
                {
                    return true; // Rejeita imediatamente
                }

                // 1.2 O próprio ConnectML (não transferir foco para si mesmo)
                if (procName.IndexOf("ConnectML", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                // 1.3 Editores de Código / Texto e Navegadores (onde 'MeasurLink' pode constar em abas)
                if (string.Equals(procName, "notepad", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "notepad++", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "devenv", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "code", StringComparison.OrdinalIgnoreCase) ||
                    procName.IndexOf("Antigravity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    string.Equals(procName, "chrome", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "msedge", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "firefox", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "brave", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(targetHint, procName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // 1.4 Janelas de infraestrutura do sistema operacional
                if (string.Equals(procName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "SearchHost", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "Taskmgr", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                bool visible = IsWindowVisible(hWnd);
                bool iconic = IsIconic(hWnd);

                // Janelas totalmente ocultas de background que não estejam minimizadas são descartadas
                if (!visible && !iconic)
                {
                    return true;
                }

                // ====================================================================
                // 2. SISTEMA DE PONTUAÇÃO DE CONFIANÇA
                // ====================================================================
                int score = 0;

                // Janela de nível superior (sem janela mãe/proprietária)
                if (GetWindow(hWnd, GW_OWNER) == IntPtr.Zero)
                {
                    score += 500;
                }

                if (isMeasurLinkTarget)
                {
                    // Verifica se o processo é genuinamente o MeasurLink da Mitutoyo
                    bool isMeasurLinkProc = procName.IndexOf("MeasurLink", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            procName.IndexOf("DataCollection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            procPath.IndexOf("MeasurLink", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            procPath.IndexOf("Mitutoyo", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isMeasurLinkProc)
                    {
                        score += 10000; // Prioridade MÁXIMA para binários executáveis do MeasurLink
                    }

                    if (title.IndexOf("MeasurLink", StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 4000;

                    if (title.IndexOf("Real-Time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("Data Collection", StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 2500;
                }
                else
                {
                    if (procName.IndexOf(targetHint, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 6000;

                    if (title.IndexOf(targetHint, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 3000;
                }

                // Bônus se tiver título descritivo preenchido
                if (!string.IsNullOrWhiteSpace(title))
                    score += 400;

                // Janela já visível tem preferência sobre minimizada
                if (visible)
                    score += 200;

                if (score > bestScore && score >= 1000)
                {
                    bestScore = score;
                    bestHwnd = hWnd;
                    bestTitle = title;
                    bestProc = procName;
                    bestClass = className;
                }

                return true;
            }, IntPtr.Zero);

            if (bestHwnd != IntPtr.Zero)
            {
                Log.Information("[Interop] Janela alvo selecionada com precisão: HWND={Hwnd:X8}, Processo='{Proc}', Classe='{Class}', Título='{Title}', Score={Score}",
                    bestHwnd.ToInt64(), bestProc, bestClass, bestTitle, bestScore);
            }
            else
            {
                Log.Warning("[Interop] Nenhuma janela de aplicação correspondente a '{Target}' encontrada no sistema.", targetHint);
            }

            return bestHwnd;
        }

        /// <summary>
        /// Mantém compatibilidade com a assinatura legada, delegando para o algoritmo robusto.
        /// </summary>
        public static IntPtr FindWindowByPartialTitle(string partialTitle)
        {
            return FindTargetWindow(partialTitle);
        }

        /// <summary>
        /// Traz a janela especificada para o primeiro plano, restaurando-a se estiver minimizada
        /// e garantindo a transferência de foco de teclado sem acionar indevidamente o menu do Windows.
        /// </summary>
        public static bool BringWindowToForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                return false;

            try
            {
                LockSetForegroundWindow(LSFW_UNLOCK);

                // Se a janela estiver minimizada, restaura
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                else
                {
                    ShowWindow(hWnd, SW_SHOW);
                }

                IntPtr foregroundHwnd = GetForegroundWindow();
                uint foregroundThreadId = GetWindowThreadProcessId(foregroundHwnd, out _);
                uint currentThreadId = GetCurrentThreadId();
                uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

                bool attachedFore = false;
                bool attachedApp = false;

                if (foregroundThreadId != 0 && foregroundThreadId != targetThreadId)
                {
                    attachedFore = AttachThreadInput(foregroundThreadId, targetThreadId, true);
                }

                if (currentThreadId != targetThreadId)
                {
                    attachedApp = AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                BringWindowToTop(hWnd);
                bool result = SetForegroundWindow(hWnd);
                SetActiveWindow(hWnd);
                SetFocus(hWnd);

                if (attachedFore)
                {
                    AttachThreadInput(foregroundThreadId, targetThreadId, false);
                }

                if (attachedApp)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }

                // Garante que a tecla Alt esteja liberada (sem estado de tecla preso)
                byte scanAlt = (byte)MapVirtualKey(VK_MENU, MAPVK_VK_TO_VSC);
                keybd_event(VK_MENU, scanAlt, KEYEVENTF_KEYUP, UIntPtr.Zero);

                return result || GetForegroundWindow() == hWnd;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Interop] Erro ao trazer janela para primeiro plano");
                return false;
            }
        }

        /// <summary>
        /// Localiza uma janela pelo alvo informado, restaura e posiciona em foco.
        /// </summary>
        public static async Task<bool> FindAndFocusWindowAsync(string partialTitle, int preDelayMs = 120)
        {
            IntPtr hWnd = FindTargetWindow(partialTitle);
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            bool wasIconic = IsIconic(hWnd);
            bool focused = BringWindowToForeground(hWnd);
            if (focused)
            {
                // Se a janela estava minimizada, aguarda tempo adicional para animação de restauração
                int delay = wasIconic ? Math.Max(preDelayMs, 180) : Math.Max(preDelayMs, 80);
                await Task.Delay(delay);
            }

            return focused;
        }
    }
}
