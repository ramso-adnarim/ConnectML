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
        private const int SW_MAXIMIZE = 3;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_MENU = 0x12; // Alt
        private const uint LSFW_UNLOCK = 2;
        private const uint GW_OWNER = 4;
        private const uint MAPVK_VK_TO_VSC = 0;

        // Constantes Win32 MDI e Mensagens de Sistema
        private const uint WM_MDIGETACTIVE = 0x0229;
        private const uint WM_MDIMAXIMIZE = 0x0225;
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MAXIMIZE = 0xF030;
        private const int GWL_STYLE = -16;
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_CAPTION = 0x00C00000;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, out int lParam);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

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
        /// Traz a janela especificada para o primeiro plano, restaurando-a se estiver minimizada,
        /// garantindo a transferência de foco sem acionar indevidamente o menu do Windows e sem
        /// desmaximizar sub-janelas MDI ativas.
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

                // NOTA CRÍTICA MDI: NÃO chamar SetActiveWindow(hWnd) nem SetFocus(hWnd) na janela mãe!
                // Em aplicações MDI (como o MeasurLink), chamar SetFocus na janela mãe retira o foco da
                // sub-janela (aba/rotina MDI) e faz com que o container MDI desmaximize/restaure a aba.
                // O SetForegroundWindow nativo já direciona o foco para o filho MDI ativo.

                if (attachedFore)
                {
                    AttachThreadInput(foregroundThreadId, targetThreadId, false);
                }

                if (attachedApp)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }

                // Libera a tecla Alt APENAS se estiver ativamente pressionada no hardware,
                // evitando disparar um evento fantasma de KEYUP que abriria o menu do sistema e desmaximizaria abas
                if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0)
                {
                    byte scanAlt = (byte)MapVirtualKey(VK_MENU, MAPVK_VK_TO_VSC);
                    keybd_event(VK_MENU, scanAlt, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }

                // Garante que a aba MDI permaneça maximizada e com o foco de teclado
                EnsureMdiChildMaximizedAndFocused(hWnd);

                return result || GetForegroundWindow() == hWnd;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Interop] Erro ao trazer janela para primeiro plano");
                return false;
            }
        }

        /// <summary>
        /// Localiza o contêiner MDIClient do MeasurLink e garante que a aba/rotina ativa esteja MAXIMIZADA
        /// e com o foco de teclado direcionado para ela (e não para a janela mãe).
        /// Se a aba tiver sido restaurada (desmaximizada) por troca de foco ou atalho mnemônico,
        /// remaximiza automaticamente via WM_MDIMAXIMIZE / SW_MAXIMIZE.
        /// </summary>
        public static bool EnsureMdiChildMaximizedAndFocused(IntPtr hWndTopLevel)
        {
            if (hWndTopLevel == IntPtr.Zero || !IsWindow(hWndTopLevel))
                return false;

            try
            {
                // 1. Procura o container MDIClient padrão do Win32/MFC
                IntPtr hMdiClient = FindWindowEx(hWndTopLevel, IntPtr.Zero, "MDIClient", null);
                if (hMdiClient != IntPtr.Zero)
                {
                    // Consulta a sub-janela MDI ativa e se está maximizada (WM_MDIGETACTIVE)
                    IntPtr hActiveChild = SendMessage(hMdiClient, WM_MDIGETACTIVE, IntPtr.Zero, out int isMaximized);
                    if (hActiveChild != IntPtr.Zero && IsWindow(hActiveChild))
                    {
                        bool isZoomed = IsZoomed(hActiveChild) || isMaximized != 0;
                        if (!isZoomed)
                        {
                            Log.Information("[Interop] Aba MDI do MeasurLink (HWND={Child:X8}) detectada em estado restaurado. Remaximizando automaticamente...", 
                                hActiveChild.ToInt64());

                            // Remaximiza via protocolo nativo Win32 MDI (WM_MDIMAXIMIZE)
                            SendMessage(hMdiClient, WM_MDIMAXIMIZE, hActiveChild, IntPtr.Zero);

                            // Reforço duplo para compatibilidade com MFC Feature Pack e tabs
                            ShowWindow(hActiveChild, SW_MAXIMIZE);
                            SendMessage(hActiveChild, WM_SYSCOMMAND, (IntPtr)SC_MAXIMIZE, IntPtr.Zero);
                        }

                        // Garante que o foco permaneça diretamente na aba ativa de medição
                        SetFocus(hActiveChild);
                        return true;
                    }
                }
                else
                {
                    // 2. Fallback: Se o MeasurLink utilizar container customizado sem MDIClient clássico,
                    // inspeciona janelas filhas visíveis com estilo de janela de documento
                    EnumChildWindows(hWndTopLevel, (hChild, lParam) =>
                    {
                        if (IsWindowVisible(hChild))
                        {
                            int style = GetWindowLong(hChild, GWL_STYLE);
                            if ((style & WS_CHILD) != 0 && (style & WS_CAPTION) == WS_CAPTION)
                            {
                                if (!IsZoomed(hChild))
                                {
                                    Log.Information("[Interop] Sub-janela/aba do MeasurLink (HWND={Child:X8}) em estado restaurado. Remaximizando...", hChild.ToInt64());
                                    ShowWindow(hChild, SW_MAXIMIZE);
                                    SendMessage(hChild, WM_SYSCOMMAND, (IntPtr)SC_MAXIMIZE, IntPtr.Zero);
                                }
                            }
                        }
                        return true;
                    }, IntPtr.Zero);
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Interop] Falha ao verificar/remaximizar aba MDI do MeasurLink");
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
                // Se a janela estava minimizada ou se for primeira ativação, aguarda estabilização de renderização
                int delay = wasIconic ? Math.Max(preDelayMs, 200) : Math.Max(preDelayMs, 100);
                await Task.Delay(delay);
            }

            return focused;
        }
    }
}
