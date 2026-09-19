using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConnectML.Core.Interfaces;
using ConnectML.Core.Models;
using Serilog;

namespace ConnectML.Infrastructure.Services
{
    /// <summary>
    /// Serviço de monitoramento serial e transporte transparente para leitores de código de barras.
    /// Opera de forma totalmente concorrente e não-bloqueante em relação à esteira principal do ConnectML.
    /// </summary>
    public class BarcodeSerialMonitorService : IBarcodeMonitorService
    {
        private readonly IBarcodeCommandHandler _commandHandler;
        private readonly object _lock = new object();
        private readonly List<BarcodeRuleConfig> _rules = new List<BarcodeRuleConfig>();

        private SerialPort? _readerPort;
        private SerialPort? _outputPort;
        private CancellationTokenSource? _internalCts;
        private Task? _readLoopTask;
        private bool _isRunning;

        private string _readerPortName = string.Empty;
        private string _outputPortName = string.Empty;

        public bool IsRunning => _isRunning;

        public event EventHandler<BarcodeTransportResult>? ReadingProcessed;

        public BarcodeSerialMonitorService(IBarcodeCommandHandler commandHandler)
        {
            _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        }

        public async Task StartMonitoringAsync(string readerPort, string outputPort, IEnumerable<BarcodeRuleConfig> rules, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(readerPort))
                throw new ArgumentException("A porta serial do leitor deve ser informada.", nameof(readerPort));

            if (string.IsNullOrWhiteSpace(outputPort))
                throw new ArgumentException("A porta serial de saída (MeasurLink) deve ser informada.", nameof(outputPort));

            if (_isRunning)
            {
                await StopMonitoringAsync();
            }

            _readerPortName = readerPort.Trim();
            _outputPortName = outputPort.Trim();

            lock (_lock)
            {
                _rules.Clear();
                if (rules != null)
                {
                    _rules.AddRange(rules);
                }
            }

            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // 1. Inicializa e abre a porta de entrada (Leitor)
                _readerPort = new SerialPort(_readerPortName)
                {
                    BaudRate = 9600,
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };
                _readerPort.Open();

                // 2. Inicializa e abre a porta de saída (MeasurLink via com0com)
                _outputPort = new SerialPort(_outputPortName)
                {
                    BaudRate = 9600,
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };
                _outputPort.Open();

                _isRunning = true;
                Log.Information("[Leitor] Monitoramento iniciado na porta {ReaderPort} com repasse para {OutputPort}", _readerPortName, _outputPortName);

                // 3. Dispara o loop assíncrono de leitura em background isolado
                _readLoopTask = Task.Run(() => ReadLoopAsync(_readerPort, _outputPort, _internalCts.Token));
            }
            catch (Exception ex)
            {
                _isRunning = false;
                CleanupPorts();
                Log.Error(ex, "[Leitor] Falha ao abrir portas seriais ({ReaderPort} -> {OutputPort}): {Message}", 
                    _readerPortName, _outputPortName, ex.Message);
                throw;
            }
        }

        public async Task StopMonitoringAsync()
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            if (_internalCts != null)
            {
                try
                {
                    _internalCts.Cancel();
                }
                catch { }
            }

            if (_readLoopTask != null)
            {
                try
                {
                    // Aguarda término do loop de forma resiliente
                    await Task.WhenAny(_readLoopTask, Task.Delay(1000));
                }
                catch { }
                _readLoopTask = null;
            }

            CleanupPorts();

            if (_internalCts != null)
            {
                _internalCts.Dispose();
                _internalCts = null;
            }

            Log.Information("[Leitor] Monitoramento serial encerrado.");
        }

        public void UpdateRules(IEnumerable<BarcodeRuleConfig> rules)
        {
            lock (_lock)
            {
                _rules.Clear();
                if (rules != null)
                {
                    _rules.AddRange(rules);
                }
            }
        }

        private async Task ReadLoopAsync(SerialPort reader, SerialPort output, CancellationToken ct)
        {
            var buffer = new byte[1024];
            var lineBuilder = new StringBuilder();

            while (!ct.IsCancellationRequested && reader.IsOpen)
            {
                try
                {
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = await reader.BaseStream.ReadAsync(buffer, 0, buffer.Length, ct);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        await Task.Delay(20, ct);
                        continue;
                    }

                    string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    foreach (char c in chunk)
                    {
                        if (c == '\r' || c == '\n')
                        {
                            if (lineBuilder.Length > 0)
                            {
                                string completeLine = lineBuilder.ToString().Trim();
                                lineBuilder.Clear();

                                if (!string.IsNullOrEmpty(completeLine))
                                {
                                    await ProcessLineAsync(completeLine, output);
                                }
                            }
                        }
                        else
                        {
                            lineBuilder.Append(c);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Log.Error("[Leitor] Erro durante leitura na porta {Port}: {Message}", _readerPortName, ex.Message);
                        await Task.Delay(200, ct);
                    }
                }
            }
        }

        private async Task ProcessLineAsync(string line, SerialPort output)
        {
            BarcodeRuleConfig? matchedRule = null;
            lock (_lock)
            {
                matchedRule = _rules.FirstOrDefault(r => 
                    string.Equals(r.Keyword.Trim(), line.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (matchedRule != null)
            {
                // Leitura INTERCEPTADA: NÃO escreve na porta de saída (MeasurLink)
                bool cmdSuccess = false;
                string? errorMessage = null;

                try
                {
                    cmdSuccess = await _commandHandler.ExecuteCommandAsync(matchedRule.CommandId, line);
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    Log.Error(ex, "[Leitor] Erro ao executar comando '{Command}' para a palavra-chave '{Keyword}'", 
                        matchedRule.CommandId, line);
                }

                var result = new BarcodeTransportResult
                {
                    RawData = line,
                    IsIntercepted = true,
                    ExecutedCommandId = matchedRule.CommandId,
                    Success = cmdSuccess,
                    ErrorMessage = errorMessage,
                    Timestamp = DateTime.Now
                };

                ReadingProcessed?.Invoke(this, result);
            }
            else
            {
                // Leitura NORMAL: Despacha para a porta COM de saída (MeasurLink)
                bool dispatchSuccess = false;
                string? errorMessage = null;

                try
                {
                    if (output.IsOpen)
                    {
                        output.WriteLine(line);
                        dispatchSuccess = true;
                        Log.Information("[Leitor] Leitura transportada para {Port}: '{Data}' [Sucesso]", _outputPortName, line);
                    }
                    else
                    {
                        errorMessage = "A porta serial de saída está fechada.";
                        Log.Error("[Leitor] Falha ao despachar para {Port}: A porta está fechada.", _outputPortName);
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    Log.Error(ex, "[Leitor] Falha ao despachar para {Port}: {Message}", _outputPortName, ex.Message);
                }

                var result = new BarcodeTransportResult
                {
                    RawData = line,
                    IsIntercepted = false,
                    Success = dispatchSuccess,
                    ErrorMessage = errorMessage,
                    Timestamp = DateTime.Now
                };

                ReadingProcessed?.Invoke(this, result);
            }
        }

        private void CleanupPorts()
        {
            if (_readerPort != null)
            {
                try
                {
                    if (_readerPort.IsOpen) _readerPort.Close();
                    _readerPort.Dispose();
                }
                catch { }
                _readerPort = null;
            }

            if (_outputPort != null)
            {
                try
                {
                    if (_outputPort.IsOpen) _outputPort.Close();
                    _outputPort.Dispose();
                }
                catch { }
                _outputPort = null;
            }
        }

        public void Dispose()
        {
            StopMonitoringAsync().GetAwaiter().GetResult();
        }
    }
}
