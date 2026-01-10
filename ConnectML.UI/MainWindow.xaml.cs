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
using System.Windows.Media.Animation;
using Microsoft.Win32;

using System.Text.Json;
using ConnectML.UI.Models;

namespace ConnectML.UI
{
    public partial class MainWindow : Window
    {
        private bool _isRunning = false;
        private FileSystemWatcher? _watcher;
        private IPlcDriver? _plcDriver;
        private const string ConfigFile = "appsettings.json";

        public MainWindow()
        {
            InitializeComponent();
            SetupLogging();
            // Driver will be initialized on StartService
            LoadSettings();
            
            // Initial Layout Check
            SizeChanged += Window_SizeChanged;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
             // 1. Auto-Collapse Logic (Smart)
             // Check if we have enough space for Main Panel (Min 600) + Logs Panel (Current Width)
             // We use a bit of margin safety (e.g., 30px)
             double minMainWidth = 630; // 600 + margins
             double availableForLogs = e.NewSize.Width - minMainWidth;
             
             // If the current Logs width doesn't fit anymore, collapse it.
             // We only trigger this if it's NOT already collapsed.
             if (!_isLogsCollapsed && ColLogs.ActualWidth > availableForLogs)
             {
                 SetLogsState(true);
             }

             // 2. Prevent Overflow (Virtual Resizing Issue)
             // We limit the MaxWidth so user can't drag it over the Main Panel space
             // MaxWidth = ActualWindowWidth - MinWidthOfConfigPanel(600) - Margins
             double maxLogsWidth = e.NewSize.Width - 630;
             
             if (maxLogsWidth < 320) maxLogsWidth = 320; // Ensure it respects min width or at least doesn't break constraint logic negatively
             
             ColLogs.MaxWidth = maxLogsWidth;
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

            // Save Settings immediately
            SaveSettings();

            if (!Directory.Exists(path))

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

                // Instantiate Real Driver with Adapter
                var logger = new ConnectML.UI.Utils.SerilogLoggerAdapter<ConnectML.Infrastructure.PlcDrivers.SiemensS7Driver>();
                _plcDriver = new ConnectML.Infrastructure.PlcDrivers.SiemensS7Driver(logger);

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

            // Animation
            var sb = (Storyboard)FindResource("BlinkAnimation");
            sb.Begin(StatusIndicator);

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

            if (_plcDriver != null)
            {
                await _plcDriver.DisconnectAsync();
            }

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

            // Stop Animation
            var sb = (Storyboard)FindResource("BlinkAnimation");
            sb.Stop(StatusIndicator);
            StatusIndicator.Opacity = 1;
            StatusIndicator.RenderTransform = new ScaleTransform(1, 1);

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

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            dialog.Title = "Selecione a pasta de monitoramento";
            dialog.Multiselect = false;
            
            if (Directory.Exists(TxtSourcePath.Text))
            {
                 dialog.DefaultDirectory = TxtSourcePath.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                TxtSourcePath.Text = dialog.FolderName;
            }
        }

        private bool _isLogsCollapsed = false;
        private GridLength? _lastLogsWidth;

        private void BtnToggleLogs_Click(object sender, RoutedEventArgs e)
        {
            SetLogsState(!_isLogsCollapsed);
        }

        private void SetLogsState(bool collapse)
        {
             if (!collapse)
             {
                 // Expand
                 PnlLogsCollapsed.Visibility = Visibility.Collapsed;
                 PnlLogsExpanded.Visibility = Visibility.Visible;
                 LogsSplitter.Visibility = Visibility.Visible;
                 LogsSplitter.IsEnabled = true;

                 ColLogs.MinWidth = 320; // Restore MinWidth constraint
                 
                 var targetWidth = _lastLogsWidth ?? new GridLength(380);
                 ColLogs.Width = targetWidth;
                 if (ColLogs.Width.Value < 320) ColLogs.Width = new GridLength(380);

                 // Auto-Resize Window if too small
                 // Minimum needed = MinMain(630) + Logs(380) approx 1010
                 double requiredWidth = 630 + ColLogs.Width.Value;
                 if (this.ActualWidth < requiredWidth)
                 {
                     this.Width = requiredWidth + 20; // Add some breathing room
                 }
                 
                 _isLogsCollapsed = false;
             }
             else
             {
                 // Collapse
                 if (!_isLogsCollapsed) _lastLogsWidth = ColLogs.Width;
                 
                 ColLogs.MinWidth = 0; // Remove constraint so it can shrink to button size
                 ColLogs.Width = GridLength.Auto;
                 PnlLogsExpanded.Visibility = Visibility.Collapsed;
                 PnlLogsCollapsed.Visibility = Visibility.Visible;
                 LogsSplitter.IsEnabled = false;
                 LogsSplitter.Visibility = Visibility.Collapsed;

                 _isLogsCollapsed = true;
             }
        }

        #region Configuration Persistence
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);

                    if (config != null)
                    {
                        TxtSourcePath.Text = config.SourcePath;
                        if (config.IsBooleanMode) RbBoolean.IsChecked = true;
                        else RbNumeric.IsChecked = true;
                        
                        // Protocol (Only 1 now, but future proof)
                        // CmbProtocol.SelectedItem ... 

                        TxtIp.Text = config.IpAddress;
                        TxtRack.Text = config.Rack;
                        TxtSlot.Text = config.Slot;
                        TxtDbBool.Text = config.DbAddressBool;
                        TxtDbInt.Text = config.DbAddressInt;
                        
                        Log.Information("Configurações carregadas com sucesso.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Erro ao carregar configurações: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var config = new AppConfig
                {
                    SourcePath = TxtSourcePath.Text,
                    IsBooleanMode = RbBoolean.IsChecked == true,
                    Protocol = CmbProtocol.Text,
                    IpAddress = TxtIp.Text,
                    Rack = TxtRack.Text,
                    Slot = TxtSlot.Text,
                    DbAddressBool = TxtDbBool.Text,
                    DbAddressInt = TxtDbInt.Text
                };

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
                Log.Information("Configurações salvas.");
            }
            catch (Exception ex)
            {
                Log.Error($"Erro ao salvar configurações: {ex.Message}");
            }
        }
        #endregion

        // Sink Class
        public class UiSink : ILogEventSink
        {
            private readonly Action<LogEvent> _action;
            public UiSink(Action<LogEvent> action) => _action = action;
            public void Emit(LogEvent logEvent) => _action(logEvent);
        }
    }
}
