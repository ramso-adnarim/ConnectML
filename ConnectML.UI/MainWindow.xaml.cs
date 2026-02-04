using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConnectML.Core;
using ConnectML.Core.Interfaces;
using ConnectML.UI.Models;
using Microsoft.Win32;
using Serilog;
using Serilog.Core;
using Serilog.Events;

// Aliases to avoid ambiguity
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ConnectML.UI
{
    public partial class MainWindow : Window
    {
        private bool _isRunning = false;
        private FileSystemWatcher? _watcher;
        private IPlcDriver? _plcDriver;
        private const string ConfigFile = "appsettings.json";

        // System Tray
        private WinForms.NotifyIcon? _notifyIcon;
        private Drawing.Icon? _defaultIcon;
        private Drawing.Icon? _activeIcon; // Icon with Green Dot
        private DispatcherTimer? _trayAnimationTimer;
        private bool _trayToggle = false;

        // Responsive Layout Memory
        private bool _isLogsCollapsed = false;
        private bool _autoHiddenBySpace = false;
        private double _userPreferredLogsWidth = 380; // Default width
        private const double MinConfigWidth = 560; // Minimum comfortable width for Config Panel
        private const double MinLogsWidth = 200;   // Minimum width before auto-collapse

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        public MainWindow()
        {
            InitializeComponent();
            SetupLogging();
            InitializeTrayIcon();
            LoadSettings();

            // Initial Layout Check
            SizeChanged += Window_SizeChanged;
            
            // Capture splitter resizing to update user preference
            PnlLogsContainer.SizeChanged += (s, e) =>
            {
               // Only update preference if we are NOT in an auto-adjustment scenario
               if (!_isLogsCollapsed && !_autoHiddenBySpace && this.ActualWidth > (MinConfigWidth + MinLogsWidth))
               {
                   if (ColLogs.ActualWidth > MinLogsWidth)
                   {
                       _userPreferredLogsWidth = ColLogs.ActualWidth;
                   }
               }
            };
        }

        private void InitializeTrayIcon()
        {
            try
            {
                // Find and load the .ico resource
                var iconUri = new Uri("pack://application:,,,/ConnectML.UI;component/ConnectML-logo-ico.ico");
                using (var stream = Application.GetResourceStream(iconUri)?.Stream)
                {
                    if (stream != null)
                    {
                        _defaultIcon = new Drawing.Icon(stream);
                    }
                }

                // Fallback if resource not found
                if (_defaultIcon == null)
                    _defaultIcon = Drawing.SystemIcons.Application;

                // Generate the Active Icon (Cache it)
                GenerateActiveIcon();

                _notifyIcon = new WinForms.NotifyIcon
                {
                    Icon = _defaultIcon,
                    Visible = true,
                    Text = "ConnectML - Monitoramento"
                };

                _notifyIcon.DoubleClick += (s, e) => RestoreWindow();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Falha ao inicializar System Tray.");
            }
        }

        private void GenerateActiveIcon()
        {
            if (_defaultIcon == null) return;

            try
            {
                // Create a bitmap from the icon
                using (var bitmap = _defaultIcon.ToBitmap())
                using (var g = Drawing.Graphics.FromImage(bitmap))
                {
                    // Draw a green circle (indicator) at bottom-right
                    var brush = new Drawing.SolidBrush(Drawing.Color.LimeGreen);
                    var pen = new Drawing.Pen(Drawing.Color.White, 2); // White border for contrast
                    
                    int size = bitmap.Width / 3;
                    int x = bitmap.Width - size - 1;
                    int y = bitmap.Height - size - 1;

                    g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillEllipse(brush, x, y, size, size);
                    g.DrawEllipse(pen, x, y, size, size);

                    // Create Icon from HIcon
                    IntPtr hIcon = bitmap.GetHicon();
                    _activeIcon = Drawing.Icon.FromHandle(hIcon);
                    
                    // We can cleanup the wrapper later, but we must decide responsibility for DestroyIcon.
                    // Since _activeIcon wraps it, we will keep it for the app lifetime and let OS cleanup or do it on Dispose.
                    // Ideally we should keep track of hIcon to destroy it when app closes.
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Erro ao gerar ícone dinâmico.");
                _activeIcon = _defaultIcon;
            }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Responsive Logic Strategy:
            // 1. We must guarantee at least MinConfigWidth (500) for the left panel.
            // 2. The remaining space is "Available for Logs".
            // 3. We cap the Logs Panel MaxWidth to this available space to prevent pushing left panel < 500.
            
            double windowWidth = e.NewSize.Width;
            double minLeftWidth = 500; 
            
            // Total available width for columns is window width - splitter width (approx 4) - margins
            // Simplified:
            double maxLogsWidth = windowWidth - minLeftWidth - 20; // 20 safety margin
            
            if (maxLogsWidth < 0) maxLogsWidth = 0;

            // Enforce MaxWidth on Logs Column to prevent it from growing too large via Splitter
            ColLogs.MaxWidth = maxLogsWidth;

            // Now handle Auto-Collapse / Auto-Show logic
            // If the calculated MaxWidth forces the Logs panel to be smaller than its MinWidth, we must collapse it.
            
            if (_isLogsCollapsed)
            {
                // Auto-Show Logic
                // If it was hidden by space, we check if we have enough space now to restore it.
                if (_autoHiddenBySpace)
                {
                    // To restore, we need at least MinLogsWidth
                     if (maxLogsWidth > MinLogsWidth)
                     {
                         _autoHiddenBySpace = false;
                         SetLogsState(false);
                     }
                }
            }
            else
            {
                // Auto-Hide Logic
                // If the constraint (MaxWidth) forces the actual width below MinLogsWidth, collapse.
                // Current ActualWidth might not have updated yet, so we check the constraint we just calculated.
                
                if (maxLogsWidth < MinLogsWidth)
                {
                    _autoHiddenBySpace = true;
                    SetLogsState(true);
                }
                else
                {
                    // Ensure the width respects the User Preference, bounded by MaxWidth
                    // If user preferred 600, but we only have 400 space (maxLogsWidth), Grid ensures MaxWidth applies.
                    
                    // However, we want the Left Panel to GROW if there is extra space.
                    // If we force ColLogs.Width = _userPreferredLogsWidth (e.g. 380), and Window is huge (1920), 
                    // Left Panel (Width="*") gets 1920 - 380 = 1540. This is desired.
                    
                    // But if window shrinks, Left Panel shrinks until 500.
                    // At that point, resizing window smaller should shrink ColLogs.
                    // Doing ColLogs.MaxWidth = ... achieves exactly this.
                    
                    // We just need to make sure ColLogs.Width is not set to a "fixed" value that fights the MaxWidth.
                    // If we set Width="380", and MaxWidth="300", effectively it is 300.
                    
                    // Only explicit update needed if we are restoring form an auto-sized state?
                    // No, XAML binding/properties handle the rest.
                }
            }
        }
        
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            // Hide to Tray
            Hide();
            // Show a balloon tip first time?
            // _notifyIcon?.ShowBalloonTip(3000, "ConnectML", "Aplicação rodando em segundo plano.", WinForms.ToolTipIcon.Info);
        }

        protected override void OnClosed(EventArgs e)
        {
            _notifyIcon?.Dispose();
            _trayAnimationTimer?.Stop();
            base.OnClosed(e);
        }

        // --- Existing Methods Preserved Below (SetupLogging, etc) ---

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
            Dispatcher.BeginInvoke(() =>
            {
                var textBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                textBlock.Inlines.Add(new System.Windows.Documents.Run($"[{logEvent.Timestamp:HH:mm:ss}] ")
                {
                    Foreground = (Brush)FindResource("TextDarker")
                });

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

                textBlock.Inlines.Add(new System.Windows.Documents.Run($" {logEvent.RenderMessage()} ")
                {
                    Foreground = (Brush)FindResource("TextSecondary")
                });

                PnlLogs.Children.Add(textBlock);

                if (PnlLogs.Parent is ScrollViewer sw)
                {
                    sw.ScrollToBottom();
                }
            });
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private async void BtnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                await StopService();
            else
                await StartService();
        }

        private async Task StartService()
        {
            string path = TxtSourcePath.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Por favor, informe o caminho da pasta.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SaveSettings();

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

                var logger = new ConnectML.UI.Utils.SerilogLoggerAdapter<ConnectML.Infrastructure.PlcDrivers.SiemensS7Driver>();
                _plcDriver = new ConnectML.Infrastructure.PlcDrivers.SiemensS7Driver(logger);

                await _plcDriver.ConnectAsync(ip, rack, slot);
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Erro ao conectar PLC: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            PnlConfiguration.IsEnabled = false;

            TxtStartStop.Text = "Parar";
            IconStartStop.Data = Geometry.Parse("M12,2C6.48,2,2,6.48,2,12s4.48,10,10,10s10-4.48,10-10S17.52,2,12,2z M16,16H8V8h8V16z");
            BtnStartStop.Background = (Brush)FindResource("StopRedBg");
            BtnStartStop.BorderBrush = (Brush)FindResource("StopRedBorder");
            TxtStartStop.Foreground = (Brush)FindResource("StopRed");
            IconStartStop.Fill = (Brush)FindResource("StopRed");

            TxtStatus.Text = "EM EXECUÇÃO";
            TxtStatus.Foreground = (Brush)FindResource("SuccessGreen");
            StatusIndicator.Fill = (Brush)FindResource("SuccessGreen");
            TxtFooterStatus.Text = "Monitorando...";
            StatusDot.Fill = (Brush)FindResource("SuccessGreen");

            var sb = (Storyboard)FindResource("BlinkAnimation");
            sb.Begin(StatusIndicator);

            _watcher = new FileSystemWatcher(path);
            _watcher.Filter = "*.*";
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            _watcher.Created += OnFileCreated;
            _watcher.EnableRaisingEvents = true;

            _isRunning = true;
            Log.Information("Serviço Iniciado. Monitorando: " + path);

            // Start Tray Animation
            StartTrayAnimation();
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

            PnlConfiguration.IsEnabled = true;

            TxtStartStop.Text = "Iniciar";
            IconStartStop.Data = Geometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z");
            BtnStartStop.Background = (Brush)FindResource("StartGreenBg");
            BtnStartStop.BorderBrush = (Brush)FindResource("StartGreenBorder");
            TxtStartStop.Foreground = (Brush)FindResource("StartGreen");
            IconStartStop.Fill = (Brush)FindResource("StartGreen");

            TxtStatus.Text = "PARADO";
            TxtStatus.Foreground = (Brush)FindResource("TextSecondary");
            StatusIndicator.Fill = (Brush)FindResource("TextSecondary");
            TxtFooterStatus.Text = "Offline";
            StatusDot.Fill = (Brush)FindResource("TextSecondary");

            var sb = (Storyboard)FindResource("BlinkAnimation");
            sb.Stop(StatusIndicator);
            StatusIndicator.Opacity = 1;
            StatusIndicator.RenderTransform = new ScaleTransform(1, 1);

            Log.Information("Serviço Parado.");

            StopTrayAnimation();
        }

        private void StartTrayAnimation()
        {
            if (_notifyIcon == null) return;
            
            _trayAnimationTimer = new DispatcherTimer();
            _trayAnimationTimer.Interval = TimeSpan.FromMilliseconds(700);
            _trayAnimationTimer.Tick += (s, e) =>
            {
                if (_notifyIcon == null) return;
                
                _trayToggle = !_trayToggle;
                _notifyIcon.Icon = _trayToggle ? _activeIcon : _defaultIcon;
            };
            _trayAnimationTimer.Start();
        }

        private void StopTrayAnimation()
        {
            if (_trayAnimationTimer != null)
            {
                _trayAnimationTimer.Stop();
                _trayAnimationTimer = null;
            }
            if (_notifyIcon != null && _defaultIcon != null)
            {
                _notifyIcon.Icon = _defaultIcon;
            }
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
             var ext = Path.GetExtension(e.FullPath).ToUpper();
             if (ext != ".QIF" && ext != ".XML") return;

             Dispatcher.InvokeAsync(async () => await ProcessFile(e.FullPath));
        }

        private async Task ProcessFile(string filePath)
        {
            try
            {
                if (!await WaitForFileReady(filePath))
                {
                    Log.Warning($"Arquivo ignorado: {Path.GetFileName(filePath)}");
                    return;
                }

                Log.Information($"Processando: {Path.GetFileName(filePath)}");
                var result = QifParser.Parse(filePath);

                if (RbBoolean.IsChecked == true)
                    await _plcDriver.WriteBoolAsync(TxtDbBool.Text, result.IsOk);
                else
                    await _plcDriver.WriteIntAsync(TxtDbInt.Text, result.FailCount);

                try
                {
                    File.Delete(filePath);
                    Log.Information("Processado e removido.");
                }
                catch (Exception ex) { Log.Error($"Erro ao deletar: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Falha no processamento.");
                try
                {
                    string dir = Path.GetDirectoryName(filePath)!;
                    string errorDir = Path.Combine(dir, "Error");
                    if (!Directory.Exists(errorDir)) Directory.CreateDirectory(errorDir);

                    string dest = Path.Combine(errorDir, Path.GetFileName(filePath));
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(filePath, dest);
                    Log.Information("Movido para Error.");
                }
                catch (Exception moveEx) { Log.Error($"Erro ao mover: {moveEx.Message}"); }
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
                            using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                                return true;
                        }
                    }
                }
                catch (IOException) { }
                catch (Exception) { }
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
            dialog.Title = "Selecione a pasta";
            if (Directory.Exists(TxtSourcePath.Text)) dialog.DefaultDirectory = TxtSourcePath.Text;

            if (dialog.ShowDialog() == true) TxtSourcePath.Text = dialog.FolderName;
        }

        private void BtnToggleLogs_Click(object sender, RoutedEventArgs e)
        {
            bool shouldCollapse = !_isLogsCollapsed;
            
            // If user manually toggles, we reset auto-hidden flags
            _autoHiddenBySpace = false;
            
            if (shouldCollapse)
            {
                 // User manually collapsing
                 SetLogsState(true);
            }
            else
            {
                 // User manually expanding
                 SetLogsState(false);
                 
                 // Restore width logic needs to be careful not to squash if window is small
                 // But user asked for it, so we try our best.
                 // Maybe resize window if needed?
                 
                 // We rely on SetLogsState to restore _userPreferredLogsWidth
            }
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

                 ColLogs.MinWidth = MinLogsWidth;
                 
                 // Use saved preference
                 double widthToRestore = _userPreferredLogsWidth < MinLogsWidth ? 380 : _userPreferredLogsWidth;
                 ColLogs.Width = new GridLength(widthToRestore);

                 // Check if this makes window too small for config
                 // But we prioritize user action here. 
                 
                 _isLogsCollapsed = false;
             }
             else
             {
                 // Collapse
                 ColLogs.MinWidth = 0;
                 ColLogs.Width = GridLength.Auto;
                 
                 PnlLogsExpanded.Visibility = Visibility.Collapsed;
                 PnlLogsCollapsed.Visibility = Visibility.Visible;
                 LogsSplitter.IsEnabled = false;
                 LogsSplitter.Visibility = Visibility.Collapsed;

                 _isLogsCollapsed = true;
             }
        }

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
                        if (config.IsBooleanMode) RbBoolean.IsChecked = true; else RbNumeric.IsChecked = true;
                        TxtIp.Text = config.IpAddress;
                        TxtRack.Text = config.Rack;
                        TxtSlot.Text = config.Slot;
                        TxtDbBool.Text = config.DbAddressBool;
                        TxtDbInt.Text = config.DbAddressInt;
                        Log.Information("Configurações carregadas.");
                    }
                }
            }
            catch (Exception ex) { Log.Error($"Erro configs: {ex.Message}"); }
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
            }
            catch (Exception ex) { Log.Error($"Erro salvar: {ex.Message}"); }
        }

        // Sink
        public class UiSink : ILogEventSink
        {
            private readonly Action<LogEvent> _action;
            public UiSink(Action<LogEvent> action) => _action = action;
            public void Emit(LogEvent logEvent) => _action(logEvent);
        }
    }
}
