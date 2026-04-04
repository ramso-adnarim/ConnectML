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
using System.Collections.Generic;
using System.Linq;
using Serilog.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ConnectML.UI.Endpoints;
using ConnectML.Infrastructure.Dispatchers;
// Aliases para evitar ambiguidade de tipos
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
        private IHost? _host;
        private const string ConfigFile = "appsettings.json";

        // System Tray (Ícone na bandeja do sistema)
        private WinForms.NotifyIcon? _notifyIcon;
        private Drawing.Icon? _defaultIcon;
        private Drawing.Icon? _activeIcon; // Ícone com indicador verde (ativo)
        private DispatcherTimer? _trayAnimationTimer;
        private bool _trayToggle = false;

        // Memória de Layout Responsivo
        private bool _isLogsCollapsed = false;
        private bool _autoHiddenBySpace = false;
        private double _userPreferredLogsWidth = 380; // Largura preferida padrão
        private const double MinConfigWidth = 350; 
        private const double IdealConfigWidth = 564;
        private const double MinLogsWidth = 300;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        public MainWindow()
        {
            InitializeComponent();
            SetupLogging();
            InitializeTrayIcon();
            LoadSettings();

            // Verificação Inicial de Layout
            SizeChanged += Window_SizeChanged;

            // Populate Virtual COM Ports
            try
            {
                var ports = System.IO.Ports.SerialPort.GetPortNames();
                foreach (var port in ports)
                {
                    bool portExists = CmbVirtualCom.Items.Cast<object>().Any(item => 
                        item.ToString()?.Equals(port, StringComparison.OrdinalIgnoreCase) == true);
                    
                    if (!portExists)
                    {
                        CmbVirtualCom.Items.Add(port);
                    }
                }
                if (CmbVirtualCom.Items.Count > 0 && string.IsNullOrEmpty(CmbVirtualCom.Text))
                {
                    CmbVirtualCom.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Não foi possível listar portas COM: {ex.Message}");
            }

            // Garante que o painel mostre a configuração correta do Webhook baseado no LoadSettings
            CmbProtocol_SelectionChanged(this, null!);
            CmbAuthType_SelectionChanged(this, null!);
            
            // Restaura estado inicial dos expanders
            ((System.Windows.Media.RotateTransform)IconToggleSource.RenderTransform).Angle = 0;
            ((System.Windows.Media.RotateTransform)IconToggleLogic.RenderTransform).Angle = 0;
            ((System.Windows.Media.RotateTransform)IconToggleIntegration.RenderTransform).Angle = 0;
            
            // Captura o redimensionamento do divisor para salvar a preferência do usuário
            PnlLogsContainer.SizeChanged += (s, e) =>
            {
               // Atualiza preferência apenas se não estiver em um cenário de ajuste automático
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
                // Carrega o recurso .ico
                var iconUri = new Uri("pack://application:,,,/ConnectML.UI;component/ConnectML-logo-ico.ico");
                using (var stream = Application.GetResourceStream(iconUri)?.Stream)
                {
                    if (stream != null)
                    {
                        _defaultIcon = new Drawing.Icon(stream);
                    }
                }

                // Fallback caso recurso não seja encontrado
                if (_defaultIcon == null)
                    _defaultIcon = Drawing.SystemIcons.Application;

                // Gera o ícone "Ativo" (Cache)
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
                // Cria bitmap a partir do ícone
                using (var bitmap = _defaultIcon.ToBitmap())
                using (var g = Drawing.Graphics.FromImage(bitmap))
                {
                    // Desenha um círculo verde (indicador) no canto inferior direito
                    var brush = new Drawing.SolidBrush(Drawing.Color.LimeGreen);
                    var pen = new Drawing.Pen(Drawing.Color.White, 2); // Borda branca para contraste
                    
                    int size = bitmap.Width / 3;
                    int x = bitmap.Width - size - 1;
                    int y = bitmap.Height - size - 1;

                    g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillEllipse(brush, x, y, size, size);
                    g.DrawEllipse(pen, x, y, size, size);

                    // Cria Ícone a partir do Handle
                    IntPtr hIcon = bitmap.GetHicon();
                    _activeIcon = Drawing.Icon.FromHandle(hIcon);
                    
                    // A limpeza do handle será gerenciada pelo sistema ou no encerramento idealmente
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
            // Estratégia de Lógica Responsiva V3.1:
            // 1. Priorizar o crescimento do Painel Direito (Logs) quando a janela for grande.
            // 2. Prevenir Sobreposição: Manter larguras mínimas (450 Esquerda, 390 Direita).
            // 3. Prioridade de Encolhimento: Logs até Mínimo -> Config até Mínimo -> Recolher Logs.

            double windowWidth = e.NewSize.Width;
            double margins = 20;
            
            // Calcula espaço para logs mantendo Configuração no "Ideal" (564)
            double targetLogsWidth = windowWidth - IdealConfigWidth - margins;
            
            // Se resultar em menos que o mínimo permitido para Logs, precisamos roubar espaço da Config ou Recolher
            if (targetLogsWidth < MinLogsWidth)
            {
                 // Verifica se podemos apenas encolher a Configuração até seu mínimo (450)
                 double spaceWithMinConfig = windowWidth - MinConfigWidth - margins;
                 
                 if (spaceWithMinConfig >= MinLogsWidth)
                 {
                     // Cenário: Janela cabe ambos nos mínimos.
                     // Define Painel Direito para Mínimo explicitamente para prevenir sobreposição.
                     // Painel Esquerdo tomará o resto (variável entre 450 e 564).
                     targetLogsWidth = MinLogsWidth;
                 }
                 else
                 {
                     // Cenário: Janela muito pequena para ambos.
                     // Hora de recolher (Collapse).
                     if (!_isLogsCollapsed)
                     {
                         _autoHiddenBySpace = true;
                         SetLogsState(true);
                     }
                     return; // Concluído
                 }
            }

            // Aplicar restrições
            if (_isLogsCollapsed)
            {
                if (_autoHiddenBySpace)
                {
                    // Podemos restaurar?
                    if ((windowWidth - margins) > (MinConfigWidth + MinLogsWidth))
                    {
                        _autoHiddenBySpace = false;
                        SetLogsState(false);
                    }
                }
            }
            else
            {
                ColLogs.Width = new GridLength(targetLogsWidth);
                ColLogs.MaxWidth = windowWidth - MinConfigWidth - margins;
            }
        }
        
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            // Ocultar para a Bandeja (Tray)
            Hide();
            // Exibir uma dica de balão na primeira vez?
            // _notifyIcon?.ShowBalloonTip(3000, "ConnectML", "Aplicação rodando em segundo plano.", WinForms.ToolTipIcon.Info);
        }

        protected override void OnClosed(EventArgs e)
        {
            _notifyIcon?.Dispose();
            _trayAnimationTimer?.Stop();
            base.OnClosed(e);
        }

        // --- Métodos Existentes Preservados Abaixo (SetupLogging, etc) ---

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
                var p = new System.Windows.Documents.Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 4)
                };

                p.Inlines.Add(new System.Windows.Documents.Run($"[{logEvent.Timestamp:HH:mm:ss}] ")
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

                p.Inlines.Add(new System.Windows.Documents.Run($" {logEvent.Level.ToString().ToUpper().Substring(0, 4)} ")
                {
                    Foreground = levelColor,
                    FontWeight = FontWeights.Bold
                });

                p.Inlines.Add(new System.Windows.Documents.Run($" {logEvent.RenderMessage()} ")
                {
                    Foreground = (Brush)FindResource("TextSecondary")
                });

                DocLogs.Blocks.Add(p);
                TxtLogsRich.ScrollToEnd();
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

            if (CmbProtocol.SelectedIndex == 1) // Webhook REST Genérico
            {
                int inboundPort = int.TryParse(TxtInboundPort.Text, out int port) ? port : 5000;
                string comPort = CmbVirtualCom.Text;

                try
                {
                    _host = Host.CreateDefaultBuilder()
                        .ConfigureWebHostDefaults(webBuilder =>
                        {
                            webBuilder.UseKestrel(options =>
                            {
                                options.ListenAnyIP(inboundPort);
                            });
                            
                            webBuilder.ConfigureServices(services =>
                            {
                                services.AddSingleton<IInboundDispatcher>(new VirtualComDispatcher(comPort));
                            });

                            webBuilder.Configure(app =>
                            {
                                app.UseRouting();
                                app.UseEndpoints(endpoints =>
                                {
                                    endpoints.MapWebhookEndpoints();
                                });
                            });
                        })
                        .Build();

                    await _host.StartAsync();
                    Log.Information($"Servidor Webhook (Inbound) Iniciado na porta {inboundPort}. Repasse Serial: {comPort}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Não foi possível iniciar o servidor Inbound na porta {inboundPort}: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else // Siemens S7
            {
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

            // Inicia a animação do ícone na bandeja
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

            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(3));
                _host.Dispose();
                _host = null;
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

                // Resolve de forma thread-safe (STA) todos os valores que vêm dos elementos de UI
                bool isWebhookMode = false;
                string webhookUrl = string.Empty;
                string webhookVerb = string.Empty;
                string authType = string.Empty;
                string authToken = string.Empty;
                string payloadTemplate = string.Empty;
                List<KeyValuePair<string, string>> customHeaders = new();

                bool isRbBooleanChecked = false;
                string txtDbBool = string.Empty;
                string txtDbInt = string.Empty;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    isWebhookMode = CmbProtocol.SelectedIndex == 1; // "Webhook REST Genérico"

                    if (isWebhookMode)
                    {
                        webhookUrl = TxtWebhookUrl.Text;
                        webhookVerb = ((ComboBoxItem)CmbWebhookVerb.SelectedItem)?.Content?.ToString() ?? "POST";
                        authType = ((ComboBoxItem)CmbAuthType.SelectedItem)?.Content?.ToString() ?? "None";
                        authToken = TxtAuthToken.Text;
                        payloadTemplate = TxtPayloadTemplate.Text;

                        customHeaders = DgCustomHeaders.Items.OfType<ConnectML.UI.Models.CustomHeader>()
                            .Where(h => !string.IsNullOrEmpty(h.Key))
                            .Select(h => new KeyValuePair<string, string>(h.Key, h.Value))
                            .ToList();
                    }
                    else
                    {
                        isRbBooleanChecked = RbBoolean.IsChecked == true;
                        txtDbBool = TxtDbBool.Text;
                        txtDbInt = TxtDbInt.Text;
                    }
                });

                if (isWebhookMode)
                {
                    // Fluxo Webhook
                    var outboundDispatcher = new ConnectML.Infrastructure.Dispatchers.WebhookOutboundDispatcher(
                        webhookUrl: webhookUrl,
                        webhookVerb: webhookVerb,
                        authType: authType,
                        authToken: authToken,
                        payloadTemplate: payloadTemplate,
                        customHeaders: customHeaders
                    );

                    await outboundDispatcher.DispatchAsync(result.IsOk, result.FailCount, result.Product);
                }
                else
                {
                    // Fluxo Antigo Siemens S7
                    if (_plcDriver != null)
                    {
                        if (isRbBooleanChecked)
                            await _plcDriver.WriteBoolAsync(txtDbBool, result.IsOk);
                        else
                            await _plcDriver.WriteIntAsync(txtDbInt, result.FailCount);
                    }
                }

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
            DocLogs.Blocks.Clear();
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tr = new System.Windows.Documents.TextRange(DocLogs.ContentStart, DocLogs.ContentEnd);
                if (!string.IsNullOrWhiteSpace(tr.Text))
                {
                    Clipboard.SetText(tr.Text);
                }
            }
            catch (Exception) { }
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
            
            // Se usuário alterna manualmente, resetamos flags de auto-ocultação
            _autoHiddenBySpace = false;
            
            if (shouldCollapse)
            {
                 // Usuário recolhendo manualmente
                 SetLogsState(true);
            }
            else
            {
                 // Usuário expandindo manualmente
                 SetLogsState(false);
                 
                 // A lógica de restauração da largura precisa ser cuidadosa para não achatar se a janela for pequena.
                 // Mas o usuário pediu, então tentamos o nosso melhor.
                 // Talvez redimensionar a janela se necessário?
                 
                 // Confiamos em SetLogsState para restaurar _userPreferredLogsWidth
            }
        }

        private void SetLogsState(bool collapse)
        {
             if (!collapse)
             {
                 // Expandir
                 _isLogsCollapsed = false; // Marca como expandido imediatamente para SizeChanged tratar corretamente
                 
                 PnlLogsCollapsed.Visibility = Visibility.Collapsed;
                 PnlLogsExpanded.Visibility = Visibility.Visible;
                 LogsSplitter.Visibility = Visibility.Visible;
                 LogsSplitter.IsEnabled = true;

                 ColLogs.MinWidth = MinLogsWidth;

                 // Calcula largura a restaurar
                 double widthToRestore = _userPreferredLogsWidth < MinLogsWidth ? MinLogsWidth : _userPreferredLogsWidth;
                 
                 // Verifica se largura atual da janela comporta isso
                 double currentWidth = this.ActualWidth;
                 double margins = 20;
                 double requiredWidth = MinConfigWidth + widthToRestore + margins;
                 
                 if (currentWidth < requiredWidth)
                 {
                     // Redimensiona Janela da Aplicação
                     this.Width = requiredWidth;
                     
                     // Remove temporariamente limite MaxWidth para permitir ajuste
                     // O evento SizeChanged disparará e ajustará limites novamente
                     ColLogs.MaxWidth = double.PositiveInfinity;
                 }
                 else
                 {
                     // Garante que MaxWidth permita a restauração
                     ColLogs.MaxWidth = currentWidth - MinConfigWidth - margins;
                 }

                 ColLogs.Width = new GridLength(widthToRestore);
             }
             else
             {
                 // Recolher
                 _isLogsCollapsed = true;
                 
                 ColLogs.MinWidth = 0;
                 ColLogs.Width = GridLength.Auto;
                 
                 PnlLogsExpanded.Visibility = Visibility.Collapsed;
                 PnlLogsCollapsed.Visibility = Visibility.Visible;
                 LogsSplitter.IsEnabled = false;
                 LogsSplitter.Visibility = Visibility.Collapsed;
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
                        
                        SelectComboBoxItemByContent(CmbProtocol, config.Protocol);
                        
                        // Siemens
                        TxtIp.Text = config.IpAddress;
                        TxtRack.Text = config.Rack;
                        TxtSlot.Text = config.Slot;
                        TxtDbBool.Text = config.DbAddressBool;
                        TxtDbInt.Text = config.DbAddressInt;
                        
                        // Inbound
                        TxtInboundPort.Text = config.InboundPort.ToString();
                        
                        if (!string.IsNullOrEmpty(config.VirtualComPort))
                        {
                            bool portExists = CmbVirtualCom.Items.Cast<object>().Any(item => 
                                item.ToString()?.Equals(config.VirtualComPort, StringComparison.OrdinalIgnoreCase) == true);
                            
                            if (!portExists)
                            {
                                CmbVirtualCom.Items.Add(config.VirtualComPort);
                            }
                        }
                        SelectComboBoxItemByContent(CmbVirtualCom, config.VirtualComPort);
                        
                        // Webhook
                        TxtWebhookUrl.Text = config.WebhookUrl;
                        SelectComboBoxItemByContent(CmbWebhookVerb, config.WebhookVerb);
                        SelectComboBoxItemByContent(CmbAuthType, config.AuthType);
                        TxtAuthToken.Text = config.AuthToken;
                        TxtPayloadTemplate.Text = config.PayloadTemplate;
                        
                        // Headers DataGrid
                        var headers = new System.Collections.ObjectModel.ObservableCollection<CustomHeader>(config.CustomHeaders ?? new System.Collections.Generic.List<CustomHeader>());
                        DgCustomHeaders.ItemsSource = headers;

                        Log.Information("Configurações carregadas.");
                    }
                }
            }
            catch (Exception ex) { Log.Error($"Erro configs: {ex.Message}"); }
        }

        private void SelectComboBoxItemByContent(ComboBox cb, string? targetValue)
        {
            if (string.IsNullOrEmpty(targetValue)) return;
            foreach (var item in cb.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Content?.ToString() == targetValue)
                {
                    cb.SelectedItem = cbi;
                    return;
                }
                else if (item is string str && str == targetValue)
                {
                    cb.SelectedItem = str;
                    return;
                }
            }
        }

        private void SaveSettings()
        {
            try
            {
                var headers = DgCustomHeaders.ItemsSource as System.Collections.ObjectModel.ObservableCollection<CustomHeader>;
                var config = new AppConfig
                {
                    SourcePath = TxtSourcePath.Text,
                    IsBooleanMode = RbBoolean.IsChecked == true,
                    Protocol = CmbProtocol.Text,
                    
                    // Siemens
                    IpAddress = TxtIp.Text,
                    Rack = TxtRack.Text,
                    Slot = TxtSlot.Text,
                    DbAddressBool = TxtDbBool.Text,
                    DbAddressInt = TxtDbInt.Text,
                    
                    // Inbound
                    InboundPort = int.TryParse(TxtInboundPort.Text, out int port) ? port : 5000,
                    VirtualComPort = CmbVirtualCom.Text,
                    
                    // Webhook
                    WebhookUrl = TxtWebhookUrl.Text,
                    WebhookVerb = CmbWebhookVerb.Text,
                    AuthType = CmbAuthType.Text,
                    AuthToken = TxtAuthToken.Text,
                    PayloadTemplate = TxtPayloadTemplate.Text,
                    CustomHeaders = headers != null ? new System.Collections.Generic.List<CustomHeader>(headers) : new System.Collections.Generic.List<CustomHeader>()
                };
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex) { Log.Error($"Erro salvar: {ex.Message}"); }
        }

        private void CmbProtocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PnlSiemensConfig == null || PnlWebhookConfig == null) return;
            
            if (CmbProtocol.SelectedIndex == 0) // Siemens
            {
                PnlSiemensConfig.Visibility = Visibility.Visible;
                PnlSiemensDbConfig.Visibility = Visibility.Visible;
                
                PnlWebhookConfig.Visibility = Visibility.Collapsed;
            }
            else // Webhook Genérico
            {
                PnlSiemensConfig.Visibility = Visibility.Collapsed;
                PnlSiemensDbConfig.Visibility = Visibility.Collapsed;
                
                PnlWebhookConfig.Visibility = Visibility.Visible;
            }
        }

        private void CmbAuthType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PnlAuthToken == null) return;
            
            if (CmbAuthType.SelectedIndex == 0) // None
            {
                PnlAuthToken.Visibility = Visibility.Collapsed;
            }
            else // Basic, Bearer, HMAC
            {
                PnlAuthToken.Visibility = Visibility.Visible;
            }
        }

        private void BtnToggleSource_Click(object sender, RoutedEventArgs e)
        {
            if (PnlSourceBody.Visibility == Visibility.Visible)
            {
                PnlSourceBody.Visibility = Visibility.Collapsed;
                ((System.Windows.Media.RotateTransform)IconToggleSource.RenderTransform).Angle = -90;
            }
            else
            {
                PnlSourceBody.Visibility = Visibility.Visible;
                ((System.Windows.Media.RotateTransform)IconToggleSource.RenderTransform).Angle = 0;
            }
        }

        private void BtnToggleLogic_Click(object sender, RoutedEventArgs e)
        {
            if (PnlLogicBody.Visibility == Visibility.Visible)
            {
                PnlLogicBody.Visibility = Visibility.Collapsed;
                ((System.Windows.Media.RotateTransform)IconToggleLogic.RenderTransform).Angle = -90;
            }
            else
            {
                PnlLogicBody.Visibility = Visibility.Visible;
                ((System.Windows.Media.RotateTransform)IconToggleLogic.RenderTransform).Angle = 0;
            }
        }

        private void BtnToggleIntegration_Click(object sender, RoutedEventArgs e)
        {
            if (PnlIntegrationBody.Visibility == Visibility.Visible)
            {
                PnlIntegrationBody.Visibility = Visibility.Collapsed;
                ((System.Windows.Media.RotateTransform)IconToggleIntegration.RenderTransform).Angle = -90;
            }
            else
            {
                PnlIntegrationBody.Visibility = Visibility.Visible;
                ((System.Windows.Media.RotateTransform)IconToggleIntegration.RenderTransform).Angle = 0;
            }
        }

        private void BtnToggleWordWrap_Click(object sender, RoutedEventArgs e)
        {
            TxtPayloadTemplate.WordWrap = !TxtPayloadTemplate.WordWrap;
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
