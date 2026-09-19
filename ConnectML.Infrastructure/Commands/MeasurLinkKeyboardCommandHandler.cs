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
    /// Suporta sequências como Alt + Q + O e é totalmente customizável via configurações.
    /// </summary>
    public class MeasurLinkKeyboardCommandHandler : IBarcodeCommandHandler
    {
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

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
                    PreDelayMs = 80
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
            bool windowFocused = await WindowFocusHelper.FindAndFocusWindowAsync(cmd.TargetWindowTitle, cmd.PreDelayMs);
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
        /// Interpreta e simula a sequência de teclas.
        /// Suporta formatos como:
        /// - "Alt+Q+O" ou "Alt + Q + O"
        /// - "%{q}{o}" ou "%qo" (Alt seguido de Q e O)
        /// - "{ENTER}", "{ESC}", "Ctrl+S"
        /// </summary>
        public static async Task SimulateKeysAsync(string keySequence)
        {
            if (string.IsNullOrWhiteSpace(keySequence))
                return;

            string normalized = keySequence.Trim();

            // Caso específico: Alt + Q + O (Desfazer no MeasurLink)
            if (IsAltQO(normalized))
            {
                await SendAltSequenceAsync(new[] { (byte)'Q', (byte)'O' });
                return;
            }

            // Caso padrão com prefixo de modificador SendKeys (%)
            if (normalized.StartsWith("%"))
            {
                // Extrai as teclas após o %
                var subKeys = ExtractSubKeys(normalized.Substring(1));
                await SendAltSequenceAsync(subKeys);
                return;
            }

            // Caso formato por tokens separados por '+' (ex: "Alt+Q+O" ou "Ctrl+Z")
            if (normalized.Contains('+'))
            {
                var parts = normalized.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(p => p.Trim().ToUpperInvariant())
                                      .ToList();

                bool holdAlt = parts.Contains("ALT");
                bool holdCtrl = parts.Contains("CTRL") || parts.Contains("CONTROL");
                bool holdShift = parts.Contains("SHIFT");

                var actionKeys = parts.Where(p => p != "ALT" && p != "CTRL" && p != "CONTROL" && p != "SHIFT").ToList();

                if (holdAlt) KeyDown(VK_MENU);
                if (holdCtrl) KeyDown(VK_CONTROL);
                if (holdShift) KeyDown(VK_SHIFT);

                await Task.Delay(30);

                foreach (var keyName in actionKeys)
                {
                    byte vk = ResolveVirtualKey(keyName);
                    if (vk != 0)
                    {
                        TapKey(vk);
                        await Task.Delay(40);
                    }
                }

                if (holdShift) KeyUp(VK_SHIFT);
                if (holdCtrl) KeyUp(VK_CONTROL);
                if (holdAlt) KeyUp(VK_MENU);

                return;
            }

            // Teclas simples como {ENTER} ou letras
            byte singleVk = ResolveVirtualKey(normalized);
            if (singleVk != 0)
            {
                TapKey(singleVk);
            }
        }

        private static bool IsAltQO(string seq)
        {
            string clean = seq.Replace(" ", "").ToUpperInvariant();
            return clean == "ALT+Q+O" || clean == "%{Q}{O}" || clean == "%QO";
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

        private static async Task SendAltSequenceAsync(byte[] vks)
        {
            // Pressiona e segura Alt
            KeyDown(VK_MENU);
            await Task.Delay(40);

            // Pressiona cada tecla da sequência
            foreach (byte vk in vks)
            {
                TapKey(vk);
                await Task.Delay(40);
            }

            // Solta Alt
            await Task.Delay(20);
            KeyUp(VK_MENU);
        }

        private static void KeyDown(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
        }

        private static void KeyUp(byte vk)
        {
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void TapKey(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static byte ResolveVirtualKey(string token)
        {
            string clean = token.Trim().Trim('{', '}').ToUpperInvariant();

            return clean switch
            {
                "ENTER" or "RETURN" => VK_RETURN,
                "ESC" or "ESCAPE" => VK_ESCAPE,
                "TAB" => VK_TAB,
                "SPACE" or "ESPACO" => VK_SPACE,
                "BACKSPACE" or "BACK" => VK_BACK,
                "DELETE" or "DEL" => VK_DELETE,
                _ => clean.Length == 1 && clean[0] >= 'A' && clean[0] <= 'Z' ? (byte)clean[0]
                   : clean.Length == 1 && clean[0] >= '0' && clean[0] <= '9' ? (byte)clean[0]
                   : (byte)0
            };
        }
    }
}
