using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ConnectML.Core.Interfaces;
using ConnectML.Core.Models;
using ConnectML.Infrastructure.Interop;
using Serilog;

namespace ConnectML.Infrastructure.Commands
{
    /// <summary>
    /// Executor de comandos via simulação de teclado e foco Win32 na interface do MeasurLink.
    /// Suporta sequências como Alt + Q + O, atalhos de Ribbon e combinações configuráveis via JSON.
    /// </summary>
    public class MeasurLinkKeyboardCommandHandler : IBarcodeCommandHandler
    {
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint MAPVK_VK_TO_VSC = 0;

        // Códigos Virtuais (Virtual Key Codes) Win32
        private const byte VK_SHIFT = 0x10;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_MENU = 0x12; // Alt
        private const byte VK_RETURN = 0x0D;
        private const byte VK_ESCAPE = 0x1B;
        private const byte VK_TAB = 0x09;
        private const byte VK_SPACE = 0x20;
        private const byte VK_BACK = 0x08;
        private const byte VK_DELETE = 0x2E;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private readonly object _lock = new object();
        private readonly List<BarcodeCommandDefinition> _commands = new List<BarcodeCommandDefinition>();

        public MeasurLinkKeyboardCommandHandler(IEnumerable<BarcodeCommandDefinition>? initialCommands = null)
        {
            if (initialCommands != null)
            {
                _commands.AddRange(initialCommands);
            }

            // Se não houver nenhum comando configurado, inicializa com o padrão "Desfazer"
            if (_commands.Count == 0)
            {
                _commands.Add(new BarcodeCommandDefinition
                {
                    Id = "undo",
                    Name = "Desfazer (Alt + Q + O)",
                    KeySequence = "%{q}{o}",
                    TargetWindowTitle = "MeasurLink",
                    PreDelayMs = 150
                });
            }
        }

        public IReadOnlyList<BarcodeCommandDefinition> GetAvailableCommands()
        {
            lock (_lock)
            {
                return _commands.ToList().AsReadOnly();
            }
        }

        public void ReloadCommands(IEnumerable<BarcodeCommandDefinition> commands)
        {
            lock (_lock)
            {
                _commands.Clear();
                _commands.AddRange(commands);
            }
        }

        public async Task<bool> ExecuteCommandAsync(string commandId, string keyword)
        {
            BarcodeCommandDefinition? cmd;
            lock (_lock)
            {
                cmd = _commands.FirstOrDefault(c => string.Equals(c.Id, commandId, StringComparison.OrdinalIgnoreCase))
                   ?? _commands.FirstOrDefault(c => string.Equals(c.Name, commandId, StringComparison.OrdinalIgnoreCase));
            }

            if (cmd == null)
            {
                Log.Warning("[Leitor] Palavra-chave '{Keyword}' interceptada, mas comando '{CommandId}' não está cadastrado.", keyword, commandId);
                return false;
            }

            // 1. Busca e foca a janela-alvo do MeasurLink
            int preDelay = cmd.PreDelayMs > 0 ? cmd.PreDelayMs : 150;
            bool windowFocused = await WindowFocusHelper.FindAndFocusWindowAsync(cmd.TargetWindowTitle, preDelay);
            if (!windowFocused)
            {
                Log.Warning("[Leitor] Palavra-chave '{Keyword}' interceptada -> Falha: Janela contendo '{Target}' não encontrada.", keyword, cmd.TargetWindowTitle);
                return false;
            }

            // 2. Simula as teclas físicas na janela focada
            try
            {
                await SimulateKeysAsync(cmd.KeySequence);
                Log.Information("[Leitor] Palavra-chave '{Keyword}' interceptada -> Comando '{Command}' ({Keys}) executado no MeasurLink [Sucesso]", 
                    keyword, cmd.Name, cmd.KeySequence);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Leitor] Erro ao simular teclas '{Keys}' para o comando '{Command}'", cmd.KeySequence, cmd.Name);
                return false;
            }
        }

        /// <summary>
        /// Interpreta e simula a sequência de teclas com injeção de Scan Codes reais.
        /// Suporta atalhos mnemônicos (Alt + Q + O), combinações simultâneas (Ctrl + Z) e toques sequenciais.
        /// </summary>
        public static async Task SimulateKeysAsync(string keySequence)
        {
            if (string.IsNullOrWhiteSpace(keySequence))
                return;

            string normalized = keySequence.Trim();

            // Caso 1: Atalho mnemônico Alt + Q + O (Desfazer no MeasurLink)
            if (IsAltQO(normalized))
            {
                await SendMnemonicSequenceAsync((byte)'Q', new[] { (byte)'O' });
                return;
            }

            // Caso 2: Notação SendKeys com prefixo de Alt (%) ex: "%{q}{o}" ou "%qo"
            if (normalized.StartsWith("%"))
            {
                var subKeys = ExtractSubKeys(normalized.Substring(1));
                if (subKeys.Length > 0)
                {
                    byte first = subKeys[0];
                    byte[] rest = subKeys.Skip(1).ToArray();
                    await SendMnemonicSequenceAsync(first, rest);
                    return;
                }
            }

            // Caso 3: Toques sequenciais separados por vírgula (ex: "Alt, Q, O")
            if (normalized.Contains(','))
            {
                var tokens = normalized.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    byte vk = ResolveVirtualKey(token.Trim());
                    if (vk != 0)
                    {
                        TapKey(vk);
                        await Task.Delay(120);
                    }
                }
                return;
            }

            // Caso 4: Notação com '+' (ex: "Alt+Q+O" ou "Ctrl+S")
            if (normalized.Contains('+'))
            {
                var parts = normalized.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(p => p.Trim().ToUpperInvariant())
                                      .ToList();

                bool hasAlt = parts.Contains("ALT");
                bool hasCtrl = parts.Contains("CTRL") || parts.Contains("CONTROL");
                bool hasShift = parts.Contains("SHIFT");

                var actionKeys = parts.Where(p => p != "ALT" && p != "CTRL" && p != "CONTROL" && p != "SHIFT")
                                      .Select(ResolveVirtualKey)
                                      .Where(vk => vk != 0)
                                      .ToList();

                // Se for sequência Alt com mais de uma tecla de ação (ex: Alt + Q + O):
                // Trata como sequência mnemônica: Alt + Primeira, solta Alt, depois as seguintes.
                if (hasAlt && actionKeys.Count >= 2)
                {
                    await SendMnemonicSequenceAsync(actionKeys[0], actionKeys.Skip(1).ToArray());
                    return;
                }

                // Combinação padrão simultânea (ex: Ctrl + Z ou Alt + F4)
                if (hasAlt) KeyDown(VK_MENU);
                if (hasCtrl) KeyDown(VK_CONTROL);
                if (hasShift) KeyDown(VK_SHIFT);

                await Task.Delay(40);

                foreach (var vk in actionKeys)
                {
                    TapKey(vk);
                    await Task.Delay(50);
                }

                if (hasShift) KeyUp(VK_SHIFT);
                if (hasCtrl) KeyUp(VK_CONTROL);
                if (hasAlt) KeyUp(VK_MENU);

                return;
            }

            // Caso 5: Tecla única simples (ex: "{ENTER}", "{ESC}", "F5")
            byte singleVk = ResolveVirtualKey(normalized);
            if (singleVk != 0)
            {
                TapKey(singleVk);
            }
        }

        private static bool IsAltQO(string seq)
        {
            string clean = seq.Replace(" ", "").ToUpperInvariant();
            return clean == "ALT+Q+O" || clean == "%{Q}{O}" || clean == "%QO" || clean == "ALT,Q,O";
        }

        /// <summary>
        /// Dispara sequências mnemônicas de menus e Ribbon:
        /// 1. Pressiona Alt + primeira tecla (ex: Alt + Q) para expandir o menu/aba;
        /// 2. Solta o Alt para que o submenu não receba Alt+Tecla subsequente;
        /// 3. Aguarda estabilização do menu;
        /// 4. Pressiona cada tecla subsequente de forma limpa (ex: 'O' para Desfazer).
        /// </summary>
        private static async Task SendMnemonicSequenceAsync(byte firstKey, byte[] subsequentKeys)
        {
            byte scanAlt = (byte)MapVirtualKey(VK_MENU, MAPVK_VK_TO_VSC);
            byte scanFirst = (byte)MapVirtualKey(firstKey, MAPVK_VK_TO_VSC);

            // Garante liberação prévia de teclas modificadoras
            keybd_event(VK_MENU, scanAlt, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(40);

            // 1. Pressiona Alt + Primeira Tecla (ex: Alt + Q)
            keybd_event(VK_MENU, scanAlt, 0, UIntPtr.Zero); // Alt DOWN
            await Task.Delay(50);

            keybd_event(firstKey, scanFirst, 0, UIntPtr.Zero); // Q DOWN
            await Task.Delay(60);
            keybd_event(firstKey, scanFirst, KEYEVENTF_KEYUP, UIntPtr.Zero); // Q UP
            await Task.Delay(50);

            // 2. SOLTA O ALT! (Fundamental para seleção de itens de submenu)
            keybd_event(VK_MENU, scanAlt, KEYEVENTF_KEYUP, UIntPtr.Zero); // Alt UP

            // 3. Aguarda a abertura e renderização do menu/Ribbon
            await Task.Delay(180);

            // 4. Pressiona as teclas subsequentes de forma limpa (ex: 'O')
            foreach (byte nextKey in subsequentKeys)
            {
                byte scanNext = (byte)MapVirtualKey(nextKey, MAPVK_VK_TO_VSC);
                keybd_event(nextKey, scanNext, 0, UIntPtr.Zero); // Tecla DOWN
                await Task.Delay(60);
                keybd_event(nextKey, scanNext, KEYEVENTF_KEYUP, UIntPtr.Zero); // Tecla UP
                await Task.Delay(80);
            }
        }

        private static byte[] ExtractSubKeys(string rest)
        {
            var keys = new List<byte>();
            int i = 0;
            while (i < rest.Length)
            {
                if (rest[i] == '{')
                {
                    int close = rest.IndexOf('}', i);
                    if (close > i)
                    {
                        string inside = rest.Substring(i + 1, close - i - 1);
                        byte vk = ResolveVirtualKey(inside);
                        if (vk != 0) keys.Add(vk);
                        i = close + 1;
                        continue;
                    }
                }

                byte directVk = ResolveVirtualKey(rest[i].ToString());
                if (directVk != 0) keys.Add(directVk);
                i++;
            }
            return keys.ToArray();
        }

        private static void KeyDown(byte vk)
        {
            byte scan = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            keybd_event(vk, scan, 0, UIntPtr.Zero);
        }

        private static void KeyUp(byte vk)
        {
            byte scan = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            keybd_event(vk, scan, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void TapKey(byte vk)
        {
            byte scan = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            keybd_event(vk, scan, 0, UIntPtr.Zero);
            keybd_event(vk, scan, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static byte ResolveVirtualKey(string token)
        {
            string clean = token.Trim().Trim('{', '}').ToUpperInvariant();

            return clean switch
            {
                "ALT" or "MENU" => VK_MENU,
                "CTRL" or "CONTROL" => VK_CONTROL,
                "SHIFT" => VK_SHIFT,
                "ENTER" or "RETURN" => VK_RETURN,
                "ESC" or "ESCAPE" => VK_ESCAPE,
                "TAB" => VK_TAB,
                "SPACE" or "ESPACO" => VK_SPACE,
                "BACKSPACE" or "BACK" => VK_BACK,
                "DELETE" or "DEL" => VK_DELETE,
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F5" => 0x74,
                "F6" => 0x75,
                "F7" => 0x76,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,
                _ => clean.Length == 1 && clean[0] >= 'A' && clean[0] <= 'Z' ? (byte)clean[0]
                   : clean.Length == 1 && clean[0] >= '0' && clean[0] <= '9' ? (byte)clean[0]
                   : (byte)0
            };
        }
    }
}
