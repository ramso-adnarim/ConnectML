using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConnectML.Core;
using ConnectML.Core.Interfaces;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ConnectML.UI
{
    public partial class MainWindow : Window
    {
        private bool _isRunning = false;
        private FileSystemWatcher? _watcher;
        private IPlcDriver _plcDriver;

        public MainWindow()
        {
            InitializeComponent();
            SetupLogging();
            _plcDriver = new MockPlcDriver();
        }

        private void SetupLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                .WriteTo.Sink(new UiSink(AddUiLog))
                .CreateLogger();

            Log.Information("Aplicação iniciada.");
        }

        private void AddUiLog(LogEvent logEvent)
        {
            // Update UI on the dispatcher thread
            Dispatcher.BeginInvoke(() =>
            {
                var textBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                // Time
                textBlock.Inlines.Add(new System.Windows.Documents.Run($"[{logEvent.Timestamp:HH:mm:ss}] ")
                {
                    Foreground = (Brush)FindResource("TextDarker")
                });

                // Level
                Brush levelColor;
                if (logEvent.Level == LogEventLevel.Error || logEvent.Level == LogEventLevel.Fatal)
                    levelColor = (Brush)FindResource("StopRed");
                else if (logEvent.Level == LogEventLevel.Warning)
                    levelColor = (Brush)FindResource("AmberColor");
                else
                    levelColor = (Brush)FindResource("BlueInfo");

                textBlock.Inlines.Add(new System.Windows.Documents.Run($" {logEvent.Level.ToString().ToUpper().Substring(0, 4)} ")
                {
                    Foreground = levelColor,
                    FontWeight = FontWeights.Bold
                });

                // Message
                textBlock.Inlines.Add(new System.Windows.Documents.Run($" {logEvent.RenderMessage()} ")
                {
                    Foreground = (Brush)FindResource("TextSecondary")
                });

                PnlLogs.Children.Add(textBlock);

                // Auto scroll
                if (PnlLogs.Parent is ScrollViewer sw)
                {
                    sw.ScrollToBottom();
                }
            });
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private async void BtnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                await StopService();
            }
            else
            {
                await StartService();
            }
        }

        private async Task StartService()
        {
            // Validation
            string path = TxtSourcePath.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Por favor, informe o caminho da pasta.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                    Log.Information($"Diretório criado: {path}");
                }
                catch (Exception ex)
                {
                     MessageBox.Show($"Não foi possível criar o diretório: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                     return;
                }
            }

            // Connect PLC
            try
            {
                string ip = TxtIp.Text;
                if (!int.TryParse(TxtRack.Text, out int rack)) rack = 0;
                if (!int.TryParse(TxtSlot.Text, out int slot)) slot = 1;

                if (string.IsNullOrWhiteSpace(TxtDbBool.Text) || string.IsNullOrWhiteSpace(TxtDbInt.Text))
                {
                     MessageBox.Show("Preencha os endereços de DB.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                     return;
                }

                await _plcDriver.ConnectAsync(ip, rack, slot);
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Erro ao conectar PLC: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            // Lock UI
            PnlConfiguration.IsEnabled = false;

            // Update Button Visuals for Stop
            TxtStartStop.Text = "Parar";
            IconStartStop.Data = Geometry.Parse("M12,2C6.48,2,2,6.48,2,12s4.48,10,10,10s10-4.48,10-10S17.52,2,12,2z M16,16H8V8h8V16z");
            BtnStartStop.Background = (Brush)FindResource("StopRedBg");
            BtnStartStop.BorderBrush = (Brush)FindResource("StopRedBorder");
            TxtStartStop.Foreground = (Brush)FindResource("StopRed");
            IconStartStop.Fill = (Brush)FindResource("StopRed");

            // Status
            TxtStatus.Text = "EM EXECUÇÃO";
            TxtStatus.Foreground = (Brush)FindResource("SuccessGreen");
            StatusIndicator.Fill = (Brush)FindResource("SuccessGreen");
            TxtFooterStatus.Text = "Monitorando...";
            StatusDot.Fill = (Brush)FindResource("SuccessGreen");

            // Watcher
            _watcher = new FileSystemWatcher(path);
            _watcher.Filter = "*.*";
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            _watcher.Created += OnFileCreated;
            _watcher.EnableRaisingEvents = true;

            _isRunning = true;
            Log.Information("Serviço Iniciado. Monitorando: " + path);
        }

        private async Task StopService()
        {
            _isRunning = false;

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            await _plcDriver.DisconnectAsync();

            // Unlock UI
            PnlConfiguration.IsEnabled = true;

            // Update Button Visuals for Start
            TxtStartStop.Text = "Iniciar";
            IconStartStop.Data = Geometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z");
            BtnStartStop.Background = (Brush)FindResource("StartGreenBg");
            BtnStartStop.BorderBrush = (Brush)FindResource("StartGreenBorder");
            TxtStartStop.Foreground = (Brush)FindResource("StartGreen");
            IconStartStop.Fill = (Brush)FindResource("StartGreen");

            // Status
            TxtStatus.Text = "PARADO";
            TxtStatus.Foreground = (Brush)FindResource("TextSecondary");
            StatusIndicator.Fill = (Brush)FindResource("TextSecondary");
            TxtFooterStatus.Text = "Offline";
            StatusDot.Fill = (Brush)FindResource("TextSecondary");

            Log.Information("Serviço Parado.");
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
             var ext = Path.GetExtension(e.FullPath).ToUpper();
             if (ext != ".QIF" && ext != ".XML") return;

             // Process in UI thread or background, using Dispatcher to switch context if needed or just task run
             // Using Dispatcher to ensure we don't block watcher thread and can update UI easily later
             Dispatcher.InvokeAsync(async () => await ProcessFile(e.FullPath));
        }

        private async Task ProcessFile(string filePath)
        {
            try
            {
                // Wait for file to be ready (exclusive access and not empty)
                if (!await WaitForFileReady(filePath))
                {
                    Log.Warning($"Arquivo ignorado (não pronto ou bloqueado): {Path.GetFileName(filePath)}");
                    return;
                }

                Log.Information($"Processando arquivo: {Path.GetFileName(filePath)}");

                // Parse
                var result = QifParser.Parse(filePath);

                // Write to PLC
                if (RbBoolean.IsChecked == true)
                {
                    string db = TxtDbBool.Text;
                    await _plcDriver.WriteBoolAsync(db, result.IsOk);
                }
                else
                {
                    string db = TxtDbInt.Text;
                    await _plcDriver.WriteIntAsync(db, result.FailCount);
                }

                // Delete file
                try
                {
                    File.Delete(filePath);
                    Log.Information("Arquivo processado e removido.");
                }
                catch (Exception ex)
                {
                    Log.Error($"Erro ao deletar arquivo: {ex.Message}");
                }

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Falha no processamento.");

                // Move to error
                try
                {
                    string dir = Path.GetDirectoryName(filePath)!;
                    string errorDir = Path.Combine(dir, "Error");
                    if (!Directory.Exists(errorDir)) Directory.CreateDirectory(errorDir);

                    string dest = Path.Combine(errorDir, Path.GetFileName(filePath));
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(filePath, dest);
                    Log.Information($"Arquivo movido para Error: {Path.GetFileName(filePath)}");
                }
                catch (Exception moveEx)
                {
                    Log.Error($"Erro ao mover arquivo para erro: {moveEx.Message}");
                }
            }
        }

        private async Task<bool> WaitForFileReady(string filePath, int timeoutMs = 10000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var info = new FileInfo(filePath);
                        if (info.Length > 0)
                        {
                            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                            {
                                return true;
                            }
                        }
                    }
                }
                catch (IOException)
                {
                    // File is locked
                }
                catch (Exception)
                {
                    // Ignore other errors
                }
                await Task.Delay(500);
            }
            return false;
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            PnlLogs.Children.Clear();
        }

        // Sink Class
        public class UiSink : ILogEventSink
        {
            private readonly Action<LogEvent> _action;
            public UiSink(Action<LogEvent> action) => _action = action;
            public void Emit(LogEvent logEvent) => _action(logEvent);
        }
    }
}
