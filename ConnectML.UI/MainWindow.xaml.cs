using System;
using System.IO;
using System.Threading;
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
using System.Windows.Interop;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ConnectML.UI
{
    public partial class MainWindow : Window
    {
        private bool _isRunning = false;
        private bool _lastRunSuccessful = false;
        private FileSystemWatcher? _watcher;
        private IPlcDriver? _plcDriver;
        private IHost? _host;
        private const string ConfigFile = "appsettings.json";
        private ObservableCollection<ConfigFieldItem> _configFields = null!;

        // System Tray (Ícone na bandeja do sistema)
        private WinForms.NotifyIcon? _notifyIcon;
        private Drawing.Icon? _defaultIcon;
        private Drawing.Icon? _activeIcon; // Ícone com indicador verde (ativo)
        private DispatcherTimer? _trayAnimationTimer;
        private bool _trayToggle = false;

        // Memória de Layout Responsivo
        private bool _isLogsCollapsed = false;
        private bool _isConfigCollapsed = false;
        private bool _autoHiddenBySpace = false;
        private bool _isLocked = false;
        private bool _testPartNumberToggle = false;
        private bool _isRetrying = false;
        private CancellationTokenSource? _retryCts;
        private AlertCountdownWindow? _activeCountdownWindow;
        private double _userPreferredLogsWidth = 380; // Largura preferida padrão
        private const double MinConfigWidth = 350; 
        private const double IdealConfigWidth = 564;
        private const double MinLogsWidth = 300;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        private static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOWME_CONNECTML");

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(new HwndSourceHook(WndProc));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SHOWME)
            {
                RestoreWindow();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public MainWindow()
        {
            InitializeComponent();
            SetupLogging();
            InitializeTrayIcon();
            
            _configFields = new ObservableCollection<ConfigFieldItem>();
            ItemsConfigList.ItemsSource = _configFields;
            
            LoadSettings();

            if (_configFields.Count == 0)
            {
                _configFields.Add(new ConfigFieldItem { FieldType = "Boolean" });
            }
            UpdateConfigFieldsState();

            // Verificação Inicial de Layout
            SizeChanged += Window_SizeChanged;
            Loaded += Window_Loaded;

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

            ApplySecurityState();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (ChkAutoStart.IsChecked == true && _lastRunSuccessful)
            {
                Log.Information("Auto-Start habilitado e última execução foi bem-sucedida. Iniciando serviço e minimizando automaticamente...");
                // Dispara o evento de start
                BtnStartStop_Click(this, new RoutedEventArgs());
                // Esconde a janela para a bandeja
                BtnMinimize_Click(this, new RoutedEventArgs());
            }
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
                _notifyIcon.BalloonTipClicked += (s, e) => RestoreWindow();
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
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
            
            // Força a janela a vir para o primeiro plano temporariamente
            var wasTopmost = Topmost;
            Topmost = true;
            Topmost = wasTopmost;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(e.NewSize.Width);
        }

        private void ApplyResponsiveLayout(double windowWidth)
        {
            if (_isConfigCollapsed)
            {
                return;
            }

            double margins = 20;
            double splitterWidth = LogsSplitter.ActualWidth > 0 ? LogsSplitter.ActualWidth : 4;
            
            // Se logs estão colapsados, o menu esquerdo ocupa 100% da largura
            if (_isLogsCollapsed)
            {
                ColConfig.Width = new GridLength(1, GridUnitType.Star);
                return;
            }

            // Verificamos se a janela comporta ambos nos tamanhos mínimos
            double minRequiredWidth = MinConfigWidth + MinLogsWidth + splitterWidth + margins;
            
            if (windowWidth < minRequiredWidth)
            {
                // Janela muito pequena -> recolhe logs automaticamente
                _autoHiddenBySpace = true;
                SetLogsState(true);
            }
            else
            {
                if (_autoHiddenBySpace)
                {
                    _autoHiddenBySpace = false;
                    SetLogsState(false);
                }

                // Mantém a Configuração na largura preferida (Pixel) ou Ideal (564)
                double configWidth = ColConfig.Width.IsAbsolute ? ColConfig.Width.Value : IdealConfigWidth;
                
                // Limita a largura da configuração para não esmagar os logs
                double maxConfigWidth = windowWidth - MinLogsWidth - splitterWidth - margins;
                if (configWidth > maxConfigWidth)
                {
                    configWidth = maxConfigWidth;
                }
                if (configWidth < MinConfigWidth)
                {
                    configWidth = MinConfigWidth;
                }

                ColConfig.Width = new GridLength(configWidth);
                ColLogs.Width = new GridLength(1, GridUnitType.Star); // Logs ocupam o restante responsivamente
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

            if (_retryCts != null)
            {
                _retryCts.Cancel();
                _retryCts.Dispose();
                _retryCts = null;
            }

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
            if (!_isLocked && e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private async void BtnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _isRetrying)
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

            // 1. Validação Webhook REST se o protocolo ativo for Webhook
            if (TxtInlineWarning != null)
            {
                TxtInlineWarning.Text = "";
                TxtInlineWarning.Visibility = Visibility.Collapsed;
            }

            if (CmbProtocol.SelectedIndex == 1) // Webhook REST Genérico
            {
                string template = TxtPayloadTemplate.Text;
                List<string> errorMessages = new List<string>();

                bool hasBoolField = _configFields.Any(f => f.FieldType == "Boolean");
                bool hasNumericField = _configFields.Any(f => f.FieldType == "Numeric");
                bool hasStringField = _configFields.Any(f => f.FieldType == "String");

                // 1. Validação: De ativos para tags (cada campo ativo deve ter sua tag no template)
                foreach (var field in _configFields)
                {
                    if (field.FieldType == "Boolean")
                    {
                        if (!ContainsTemplateTag(template, "IsOk") && !ContainsTemplateTag(template, "Status"))
                        {
                            errorMessages.Add("- O campo 'Verdadeiro/Falso' está ativo, adicione {{ IsOk }} ou {{ Status }} no payload.");
                        }
                    }
                    else if (field.FieldType == "Numeric")
                    {
                        if (!ContainsTemplateTag(template, "FailCount") && !ContainsTemplateTag(template, "Run"))
                        {
                            errorMessages.Add("- O campo 'Contador' está ativo, adicione {{ FailCount }} ou {{ Run }} no payload.");
                        }
                    }
                    else if (field.FieldType == "String")
                    {
                        if (!ContainsTemplateTag(template, "PartNumber") && !ContainsTemplateTag(template, "Product") && !ContainsTemplateTag(template, "Routine"))
                        {
                            errorMessages.Add("- O campo 'Nome da Peça' está ativo, adicione {{ PartNumber }} no payload.");
                        }
                    }
                }

                // 2. Validação: De tags para ativos (se uma tag está no template, o campo correspondente deve estar ativo)
                if (ContainsTemplateTag(template, "IsOk") || ContainsTemplateTag(template, "Status"))
                {
                    if (!hasBoolField)
                    {
                        errorMessages.Add("- A tag de status (IsOk/Status) está no payload, mas o campo 'Verdadeiro/Falso' não está ativo.");
                    }
                }
                if (ContainsTemplateTag(template, "FailCount") || ContainsTemplateTag(template, "Run"))
                {
                    if (!hasNumericField)
                    {
                        errorMessages.Add("- A tag de contagem (FailCount/Run) está no payload, mas o campo 'Contador' não está ativo.");
                    }
                }
                if (ContainsTemplateTag(template, "PartNumber") || ContainsTemplateTag(template, "Product") || ContainsTemplateTag(template, "Routine"))
                {
                    if (!hasStringField)
                    {
                        errorMessages.Add("- A tag de peça (PartNumber/Product) está no payload, mas o campo 'Nome da Peça (Part Number)' não está ativo.");
                    }
                }

                if (errorMessages.Any())
                {
                    if (TxtInlineWarning != null)
                    {
                        TxtInlineWarning.Text = "Erro de Validação de Configuração:\n" + string.Join("\n", errorMessages);
                        TxtInlineWarning.Visibility = Visibility.Visible;
                    }
                    return; // Bloqueia o início do serviço
                }
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
                                string webhookAuthType = ((ComboBoxItem)CmbAuthType.SelectedItem)?.Content?.ToString() ?? "None";
                                string webhookAuthToken = TxtAuthToken.Text;
                                string hmacHeaderName = string.IsNullOrWhiteSpace(TxtHmacHeaderName.Text) ? "X-Hub-Signature-256" : TxtHmacHeaderName.Text;

                                services.AddSingleton<IInboundDispatcher>(new VirtualComDispatcher(comPort));
                                services.AddSingleton(new ConnectML.UI.Models.WebhookInboundConfig(webhookAuthType, webhookAuthToken, hmacHeaderName));
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
                string ip = TxtIp.Text;
                if (!int.TryParse(TxtRack.Text, out int rack)) rack = 0;
                if (!int.TryParse(TxtSlot.Text, out int slot)) slot = 1;

                try
                {

                    bool hasBool = _configFields.Any(f => f.FieldType == "Boolean");
                    bool hasNumeric = _configFields.Any(f => f.FieldType == "Numeric");
                    bool hasString = _configFields.Any(f => f.FieldType == "String");

                    if ((hasBool && string.IsNullOrWhiteSpace(TxtDbBool.Text)) ||
                        (hasNumeric && string.IsNullOrWhiteSpace(TxtDbInt.Text)) ||
                        (hasString && string.IsNullOrWhiteSpace(TxtDbPartNumber.Text)) ||
                        string.IsNullOrWhiteSpace(TxtDbStatus.Text))
                    {
                         MessageBox.Show("Por favor, preencha todos os endereços de DB para os campos ativos e status.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                         return;
                    }

                    var logger = new ConnectML.UI.Utils.SerilogLoggerAdapter<ConnectML.Infrastructure.PlcDrivers.SiemensS7Driver>();
                    _plcDriver = new ConnectML.Infrastructure.PlcDrivers.SiemensS7Driver(logger);

                    await _plcDriver.ConnectAsync(ip, rack, slot);
                }
                catch (Exception ex)
                {
                     Log.Warning(ex, $"[Startup] Falha na conexão inicial com o CLP no IP {ip}. Iniciando fluxo de retentativa regressiva...");
                     _ = HandlePlcConnectionFailureAsync(ip, rack, slot, path);
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
            _lastRunSuccessful = true;
            SaveSettings();
            Log.Information("Serviço Iniciado. Monitorando: " + path);
        }

        private async Task StopService()
        {
            _isRunning = false;
            _isRetrying = false;

            // Fecha a janela de contagem ativa se ela estiver aberta
            if (_activeCountdownWindow != null)
            {
                try
                {
                    _activeCountdownWindow.Close();
                }
                catch { }
                _activeCountdownWindow = null;
            }

            if (_retryCts != null)
            {
                _retryCts.Cancel();
                _retryCts.Dispose();
                _retryCts = null;
            }

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

            _lastRunSuccessful = false;
            SaveSettings();
            Log.Information("Serviço Parado.");

            StopTrayAnimation();
        }

        private async Task HandlePlcConnectionFailureAsync(string ip, int rack, int slot, string path)
        {
            // Evita disparar multiplas retentativas concorrentes
            if (_isRetrying) return;

            // Ativa o estado de retentativa imediatamente para que cliques em "Parar" cancelem
            _isRetrying = true;

            // Desabilita as configurações pois iniciou o processo de ativação/retentativa
            PnlConfiguration.IsEnabled = false;

            // Configura o visual do botão StartStop para "Parar" (permitir cancelar)
            TxtStartStop.Text = "Parar";
            IconStartStop.Data = Geometry.Parse("M12,2C6.48,2,2,6.48,2,12s4.48,10,10,10s10-4.48,10-10S17.52,2,12,2z M16,16H8V8h8V16z");
            BtnStartStop.Background = (Brush)FindResource("StopRedBg");
            BtnStartStop.BorderBrush = (Brush)FindResource("StopRedBorder");
            TxtStartStop.Foreground = (Brush)FindResource("StopRed");
            IconStartStop.Fill = (Brush)FindResource("StopRed");

            // Exibe status temporário de aguardando
            TxtStatus.Text = "AGUARDANDO RETRY";
            TxtStatus.Foreground = (Brush)FindResource("AmberColor");
            StatusIndicator.Fill = (Brush)FindResource("AmberColor");
            TxtFooterStatus.Text = "Falha na conexão inicial...";
            StatusDot.Fill = (Brush)FindResource("AmberColor");

            // Abre a janela de alerta customizada de contagem regressiva de 6 segundos de forma NÃO-BLOQUEANTE
            var countdownWin = new AlertCountdownWindow();
            countdownWin.Owner = this;

            // Salva referência na classe para permitir fechamento remoto em caso de "Parar"
            _activeCountdownWindow = countdownWin;

            countdownWin.Show();

            bool shouldRetry = await countdownWin.CountdownTask;

            // Limpa a referência da janela
            _activeCountdownWindow = null;

            if (!shouldRetry)
            {
                // Customer clicou em "Cancelar" ou fechou a contagem regressiva ou clicou no "Parar" principal
                Log.Information("[Auto-Start Retry] Retentativas de conexão canceladas pelo customer.");
                await StopService();

                // Garante que a interface principal reapareça (ex: Auto-Start cancelado)
                RestoreWindow();
                return;
            }

            // Garante que a janela principal reapareça no primeiro plano para mostrar o status de retentativa ativa
            RestoreWindow();

            // Altera status visual para retentativa progressiva ativa
            TxtStatus.Text = "RETENTANDO CONEXÃO";
            TxtStatus.Foreground = (Brush)FindResource("AmberColor");
            StatusIndicator.Fill = (Brush)FindResource("AmberColor");
            TxtFooterStatus.Text = "Conectando ao CLP a cada 5s...";
            StatusDot.Fill = (Brush)FindResource("AmberColor");

            var sb = (Storyboard)FindResource("BlinkAnimation");
            sb.Begin(StatusIndicator);

            _retryCts = new CancellationTokenSource();
            var token = _retryCts.Token;

            // Executa o loop de retentativas em segundo plano para não congelar a interface
            _ = Task.Run(async () => await RunProgressiveRetryLoopAsync(ip, rack, slot, path, token), token);
        }

        private async Task RunProgressiveRetryLoopAsync(string ip, int rack, int slot, string path, CancellationToken token)
        {
            int attempts = 0;
            while (!token.IsCancellationRequested)
            {
                attempts++;
                Log.Information($"[Auto-Start Retry] Tentativa de conexão #{attempts} com o CLP em {ip}...");

                try
                {
                    // Tenta conectar ao CLP
                    await _plcDriver!.ConnectAsync(ip, rack, slot);

                    // Conexão bem-sucedida! Finaliza o retry com sucesso
                    if (token.IsCancellationRequested) return;

                    await Dispatcher.InvokeAsync(async () =>
                    {
                        _isRetrying = false;
                        if (_retryCts != null)
                        {
                            _retryCts.Dispose();
                            _retryCts = null;
                        }

                        // Configura o Watcher para monitorar a pasta
                        _watcher = new FileSystemWatcher(path);
                        _watcher.Filter = "*.*";
                        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
                        _watcher.Created += OnFileCreated;
                        _watcher.EnableRaisingEvents = true;

                        _isRunning = true;
                        _lastRunSuccessful = true;
                        SaveSettings();

                        // Atualiza UI para executando (Verde)
                        TxtStatus.Text = "EM EXECUÇÃO";
                        TxtStatus.Foreground = (Brush)FindResource("SuccessGreen");
                        StatusIndicator.Fill = (Brush)FindResource("SuccessGreen");
                        TxtFooterStatus.Text = "Monitorando...";
                        StatusDot.Fill = (Brush)FindResource("SuccessGreen");

                        var sb = (Storyboard)FindResource("BlinkAnimation");
                        sb.Begin(StatusIndicator);

                        Log.Information($"[Auto-Start Retry] Conexão com o CLP reestabelecida com sucesso na tentativa #{attempts}! Monitoramento ativado: {path}");

                        StartTrayAnimation();

                        // Auto-minimiza para a bandeja do sistema silenciosamente
                        BtnMinimize_Click(this, new RoutedEventArgs());
                    });

                    return; // Sai do loop
                }
                catch (Exception ex)
                {
                    // Falhou, loga apenas no Serilog/UI log block sem popup!
                    Log.Warning($"[Auto-Start Retry] Tentativa #{attempts} de conexão falhou. Nova retentativa em 5s. Detalhes: {ex.Message}");
                }

                try
                {
                    // Aguarda 5 segundos antes da próxima tentativa
                    await Task.Delay(5000, token);
                }
                catch (TaskCanceledException)
                {
                    // Cancelamento acionado durante o delay
                    break;
                }
            }
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
            bool isWebhookMode = false;
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
                string webhookUrl = string.Empty;
                string webhookVerb = string.Empty;
                string authType = string.Empty;
                string authToken = string.Empty;
                string hmacHeaderName = string.Empty;
                string payloadTemplate = string.Empty;
                List<KeyValuePair<string, string>> customHeaders = new();

                bool hasBoolField = false;
                bool hasNumericField = false;
                bool hasStringField = false;
                string txtDbBool = string.Empty;
                string txtDbInt = string.Empty;
                string txtDbPartNumber = string.Empty;
                string txtDbStatus = string.Empty;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    isWebhookMode = CmbProtocol.SelectedIndex == 1; // "Webhook REST Genérico"

                    hasBoolField = _configFields.Any(f => f.FieldType == "Boolean");
                    hasNumericField = _configFields.Any(f => f.FieldType == "Numeric");
                    hasStringField = _configFields.Any(f => f.FieldType == "String");

                    if (isWebhookMode)
                    {
                        webhookUrl = TxtWebhookUrl.Text;
                        webhookVerb = ((ComboBoxItem)CmbWebhookVerb.SelectedItem)?.Content?.ToString() ?? "POST";
                        authType = ((ComboBoxItem)CmbAuthType.SelectedItem)?.Content?.ToString() ?? "None";
                        authToken = TxtAuthToken.Text;
                        hmacHeaderName = string.IsNullOrWhiteSpace(TxtHmacHeaderName.Text) ? "X-Hub-Signature-256" : TxtHmacHeaderName.Text;
                        payloadTemplate = TxtPayloadTemplate.Text;

                        customHeaders = DgCustomHeaders.Items.OfType<ConnectML.UI.Models.CustomHeader>()
                            .Where(h => !string.IsNullOrEmpty(h.Key))
                            .Select(h => new KeyValuePair<string, string>(h.Key, h.Value))
                            .ToList();
                    }
                    else
                    {
                        txtDbBool = TxtDbBool.Text;
                        txtDbInt = TxtDbInt.Text;
                        txtDbPartNumber = TxtDbPartNumber.Text;
                        txtDbStatus = TxtDbStatus.Text;
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
                        hmacHeaderName: hmacHeaderName,
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
                        if (hasBoolField && !string.IsNullOrWhiteSpace(txtDbBool))
                            await _plcDriver.WriteBoolAsync(txtDbBool, result.IsOk);
                        
                        if (hasNumericField && !string.IsNullOrWhiteSpace(txtDbInt))
                            await _plcDriver.WriteIntAsync(txtDbInt, result.FailCount);

                        if (hasStringField && !string.IsNullOrWhiteSpace(txtDbPartNumber))
                            await _plcDriver.WriteStringAsync(txtDbPartNumber, result.Product);
                            
                        if (!string.IsNullOrWhiteSpace(txtDbStatus))
                        {
                            Log.Information($"Escrevendo Handshake Status 1 no endereço {txtDbStatus}...");
                            await _plcDriver.WriteBoolAsync(txtDbStatus, true);
                        }
                    }
                }

                try
                {
                    File.Delete(filePath);
                    Log.Information("Processado e removido.");
                    
                    // Notificação de balão no System Tray (apenas se minimizada/na bandeja)
                    if (WindowState == WindowState.Minimized || !IsVisible)
                    {
                        _notifyIcon?.ShowBalloonTip(3000, "Arquivo Processado", $"O arquivo foi processado com sucesso.\nPeça (Part Number): {result.Product}", WinForms.ToolTipIcon.Info);
                    }
                }
                catch (Exception ex) { Log.Error($"Erro ao deletar: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Falha no processamento.");
                
                // Notificação de balão de erro crítico no System Tray (apenas se minimizada/na bandeja)
                if (WindowState == WindowState.Minimized || !IsVisible)
                {
                    string errorMsg = isWebhookMode ? "Erro ao enviar Webhook." : "Erro de comunicação com o CLP.";
                    _notifyIcon?.ShowBalloonTip(4000, "Falha Crítica", $"{errorMsg} Detalhes: {ex.Message}", WinForms.ToolTipIcon.Error);
                }

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

        private void BtnHamburger_Click(object sender, RoutedEventArgs e)
        {
            _isConfigCollapsed = !_isConfigCollapsed;
            
            if (_isConfigCollapsed)
            {
                // Garante que o painel de logs esteja visível se estivesse recolhido ANTES de configurar as larguras colapsadas
                if (_isLogsCollapsed)
                {
                    SetLogsState(false);
                }

                // Recolhe as configurações e permite logs ocuparem 100% da janela
                ColConfig.MinWidth = 0;
                ColConfig.Width = new GridLength(0);
                
                // Oculta o Splitter
                LogsSplitter.Visibility = Visibility.Collapsed;
                
                // Oculta o botão de recolher logs para evitar estados inválidos
                BtnToggleLogs.Visibility = Visibility.Collapsed;
                
                // Permite que a coluna de logs se expanda flexivelmente
                ColLogs.MinWidth = 0;
                ColLogs.MaxWidth = double.PositiveInfinity;
                ColLogs.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                // Reabilita o botão de recolher logs
                BtnToggleLogs.Visibility = Visibility.Visible;
                
                if (!_isLogsCollapsed)
                {
                    LogsSplitter.Visibility = Visibility.Visible;
                    ColLogs.MinWidth = MinLogsWidth;
                    ColLogs.MaxWidth = double.PositiveInfinity;
                    
                    // Calcula a largura da configuração para que os logs tenham _userPreferredLogsWidth
                    double configWidth = this.ActualWidth - _userPreferredLogsWidth - (LogsSplitter.ActualWidth > 0 ? LogsSplitter.ActualWidth : 4) - 20;
                    if (configWidth < MinConfigWidth) configWidth = MinConfigWidth;
                    
                    ColConfig.MinWidth = MinConfigWidth;
                    ColConfig.Width = new GridLength(configWidth);
                    ColLogs.Width = new GridLength(1, GridUnitType.Star);
                }
                else
                {
                    ColConfig.MinWidth = MinConfigWidth;
                    ColConfig.Width = new GridLength(1, GridUnitType.Star);
                    
                    ColLogs.MinWidth = 0;
                    ColLogs.Width = GridLength.Auto;
                }
                
                // Força a reavaliação responsiva
                ApplyResponsiveLayout(this.ActualWidth);
            }
        }

        private void LogsSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (_isConfigCollapsed || _isLogsCollapsed) return;

            double currentWidth = ColConfig.ActualWidth;
            double newWidth = currentWidth + e.HorizontalChange;

            // Define os limites rígidos do divisor
            double minWidth = MinConfigWidth; // 350
            double margins = 20;
            double splitterWidth = LogsSplitter.ActualWidth > 0 ? LogsSplitter.ActualWidth : 4;
            double maxWidth = this.ActualWidth - MinLogsWidth - splitterWidth - margins;

            if (maxWidth < minWidth) maxWidth = minWidth;

            // Clampa a largura desejada dentro dos limites
            if (newWidth < minWidth) newWidth = minWidth;
            if (newWidth > maxWidth) newWidth = maxWidth;

            // Atualiza a largura da configuração em Pixels
            ColConfig.Width = new GridLength(newWidth, GridUnitType.Pixel);

            // Garante que a coluna de logs continue sendo Star (*) para preenchimento fluído
            ColLogs.Width = new GridLength(1, GridUnitType.Star);
        }

        private async void BtnSecurityLock_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLocked)
            {
                // Bloqueia imediatamente
                _isLocked = true;
                ApplySecurityState();
                Log.Information("[Segurança] Aplicativo bloqueado pelo customer.");
            }
            else
            {
                // Abre popup de credenciais
                var unlockWin = new SecurityUnlockWindow();
                unlockWin.Owner = this;

                // Mostra a janela e aguarda a resposta
                bool success = await unlockWin.UnlockTask;
                if (success)
                {
                    _isLocked = false;
                    ApplySecurityState();
                    Log.Information("[Segurança] Aplicativo desbloqueado com sucesso com credenciais administrativas.");
                }
            }
        }

        private void ApplySecurityState()
        {
            // Ativa ou desativa elementos baseado no status de bloqueio
            bool isEnabled = !_isLocked;

            // 1. Cadeado Visual
            if (_isLocked)
            {
                // Ícone fechado, cor Amber
                PathPadlock.Fill = (Brush)FindResource("AmberColor");
                PathPadlock.Data = Geometry.Parse("M18,8H17V6A5,5 0 0,0 12,1A5,5 0 0,0 7,6V8H6A2,2 0 0,0 4,10V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V10A2,2 0 0,0 18,8M12,17C10.89,17 10,16.1 10,15C10,13.89 10.89,13 12,13C13.11,13 14,13.89 14,15C14,16.1 13.11,17 12,17M15,8H9V6A3,3 0 0,1 12,3A3,3 0 0,1 15,6V8Z");
                BtnSecurityLock.ToolTip = "Segurança da Aplicação (Bloqueado - Clique para Desbloquear)";
            }
            else
            {
                // Ícone aberto, cor TextSecondary
                PathPadlock.Fill = (Brush)FindResource("TextSecondary");
                PathPadlock.Data = Geometry.Parse("M18,8H10V6A2,2 0 0,1 12,4C13.1,4 14,4.9 14,6H16A4,4 0 0,0 12,2A4,4 0 0,0 8,6V8H6A2,2 0 0,0 4,10V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V10A2,2 0 0,0 18,8M12,17A2,2 0 1,1 14,15A2,2 0 0,1 12,17Z");
                BtnSecurityLock.ToolTip = "Segurança da Aplicação (Desbloqueado - Clique para Bloquear)";
            }

            // 2. Bloqueio de Inputs e Painéis de Configurações
            if (_isLocked)
            {
                PnlConfiguration.IsEnabled = false;
            }
            else
            {
                PnlConfiguration.IsEnabled = !_isRunning;
            }

            // 3. Bloqueio de Botões de Ação
            BtnHamburger.IsEnabled = isEnabled;
            BtnStartStop.IsEnabled = isEnabled;
            BtnClearLogs.IsEnabled = isEnabled;
            BtnClose.IsEnabled = isEnabled;
            ChkAutoStart.IsEnabled = isEnabled;
            BtnToggleLogs.IsEnabled = isEnabled;

            // 4. Bloqueio de Divisor (Thumb)
            LogsSplitter.IsEnabled = isEnabled;

            // 5. Bloqueio de Redimensionamento da Janela
            this.ResizeMode = _isLocked ? ResizeMode.NoResize : ResizeMode.CanResize;
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
                 ColLogs.MaxWidth = double.PositiveInfinity;

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
                     currentWidth = requiredWidth;
                 }

                 // Configura a largura da configuração para que a coluna de logs tenha exatamente a largura restaurada
                 double configWidth = currentWidth - widthToRestore - (LogsSplitter.ActualWidth > 0 ? LogsSplitter.ActualWidth : 4) - margins;
                 if (configWidth < MinConfigWidth) configWidth = MinConfigWidth;

                 ColConfig.Width = new GridLength(configWidth);
                 ColLogs.Width = new GridLength(1, GridUnitType.Star);
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

                 // A configuração passa a ocupar 100% da largura
                 ColConfig.Width = new GridLength(1, GridUnitType.Star);
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
                        _configFields.Clear();
                        if (config.ConfigFields != null && config.ConfigFields.Count > 0)
                        {
                            foreach (var field in config.ConfigFields)
                            {
                                _configFields.Add(new ConfigFieldItem { FieldType = field });
                            }
                        }
                        else
                        {
                            _configFields.Add(new ConfigFieldItem { FieldType = config.IsBooleanMode ? "Boolean" : "Numeric" });
                        }
                        
                        ChkAutoStart.IsChecked = config.AutoStartEnabled;
                        _lastRunSuccessful = config.LastRunSuccessful;
                        
                        SelectComboBoxItemByContent(CmbProtocol, config.Protocol);
                        
                        // Siemens
                        TxtIp.Text = config.IpAddress;
                        TxtRack.Text = config.Rack;
                        TxtSlot.Text = config.Slot;
                        TxtDbBool.Text = config.DbAddressBool;
                        TxtDbInt.Text = config.DbAddressInt;
                        TxtDbPartNumber.Text = config.DbAddressPartNumber ?? "DB10.6";
                        TxtDbStatus.Text = config.DbAddressStatus;
                        
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
                        TxtHmacHeaderName.Text = config.HmacHeaderName;
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
                    IsBooleanMode = _configFields.Any(f => f.FieldType == "Boolean"),
                    ConfigFields = _configFields.Select(f => f.FieldType).ToList(),
                    Protocol = CmbProtocol.Text,
                    AutoStartEnabled = ChkAutoStart.IsChecked == true,
                    LastRunSuccessful = _lastRunSuccessful,
                    
                    // Siemens
                    IpAddress = TxtIp.Text,
                    Rack = TxtRack.Text,
                    Slot = TxtSlot.Text,
                    DbAddressBool = TxtDbBool.Text,
                    DbAddressInt = TxtDbInt.Text,
                    DbAddressPartNumber = TxtDbPartNumber.Text,
                    DbAddressStatus = TxtDbStatus.Text,
                    
                    // Inbound
                    InboundPort = int.TryParse(TxtInboundPort.Text, out int port) ? port : 5000,
                    VirtualComPort = CmbVirtualCom.Text,
                    
                    // Webhook
                    WebhookUrl = TxtWebhookUrl.Text,
                    WebhookVerb = CmbWebhookVerb.Text,
                    AuthType = CmbAuthType.Text,
                    AuthToken = TxtAuthToken.Text,
                    HmacHeaderName = string.IsNullOrWhiteSpace(TxtHmacHeaderName.Text) ? "X-Hub-Signature-256" : TxtHmacHeaderName.Text,
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
            if (TxtInlineWarning != null)
            {
                TxtInlineWarning.Text = "";
                TxtInlineWarning.Visibility = Visibility.Collapsed;
            }
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
                if (PnlHmacHeader != null) PnlHmacHeader.Visibility = Visibility.Collapsed;
            }
            else // Basic, Bearer, HMAC
            {
                PnlAuthToken.Visibility = Visibility.Visible;
                if (PnlHmacHeader != null)
                {
                    PnlHmacHeader.Visibility = CmbAuthType.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
                }
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

        private async void BtnTestBool_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isRunning) return;
                string dbAddress = TxtDbBool.Text;
                if (string.IsNullOrWhiteSpace(dbAddress)) return;

                Log.Information($"[Transient] Iniciando teste em {dbAddress}...");
                await RunTransientTestAsync(async (driver) => 
                {
                    bool current = await driver.ReadBoolAsync(dbAddress);
                    bool next = !current;
                    await driver.WriteBoolAsync(dbAddress, next);
                    Log.Information($"[Transient] {dbAddress} foi de {current} para {next}");
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[Transient] Erro crítico no teste da DB Booleana ({TxtDbBool.Text}): {ex.Message}");
            }
        }

        private async void BtnTestInt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isRunning) return;
                string dbAddress = TxtDbInt.Text;
                if (string.IsNullOrWhiteSpace(dbAddress)) return;

                Log.Information($"[Transient] Iniciando teste em {dbAddress}...");
                await RunTransientTestAsync(async (driver) => 
                {
                    int current = await driver.ReadIntAsync(dbAddress);
                    int next = current + 1;
                    await driver.WriteIntAsync(dbAddress, next);
                    Log.Information($"[Transient] {dbAddress} foi de {current} para {next}");
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[Transient] Erro crítico no teste da DB Inteira ({TxtDbInt.Text}): {ex.Message}");
            }
        }

        private async void BtnTestStatus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isRunning) return;
                string dbAddress = TxtDbStatus.Text;
                if (string.IsNullOrWhiteSpace(dbAddress)) return;

                Log.Information($"[Transient] Iniciando teste em {dbAddress}...");
                await RunTransientTestAsync(async (driver) => 
                {
                    bool current = await driver.ReadBoolAsync(dbAddress);
                    Log.Information($"[Transient] {dbAddress} lido como {current}");
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[Transient] Erro crítico no teste da DB Status ({TxtDbStatus.Text}): {ex.Message}");
            }
        }

        private async void BtnTestPartNumber_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isRunning) return;
                string dbAddress = TxtDbPartNumber.Text;
                if (string.IsNullOrWhiteSpace(dbAddress)) return;

                _testPartNumberToggle = !_testPartNumberToggle;
                string nextVal = _testPartNumberToggle ? "True" : "False";

                Log.Information($"[Transient] Iniciando teste de escrita de string em {dbAddress} com valor \"{nextVal}\"...");
                await RunTransientTestAsync(async (driver) => 
                {
                    await driver.WriteStringAsync(dbAddress, nextVal);
                    Log.Information($"[Transient] {dbAddress} escrito com sucesso: \"{nextVal}\"");
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[Transient] Erro crítico no teste do DB PartNumber ({TxtDbPartNumber.Text}): {ex.Message}");
            }
        }

        private async Task RunTransientTestAsync(Func<IPlcDriver, Task> testAction)
        {
            string ip = TxtIp.Text;
            int.TryParse(TxtRack.Text, out int rack);
            int.TryParse(TxtSlot.Text, out int slot);

            var logger = new ConnectML.UI.Utils.SerilogLoggerAdapter<ConnectML.Infrastructure.PlcDrivers.SiemensS7Driver>();
            var driver = new ConnectML.Infrastructure.PlcDrivers.SiemensS7Driver(logger);

            try
            {
                await driver.ConnectAsync(ip, rack, slot);
                await testAction(driver);
            }
            catch (Exception ex)
            {
                Log.Error($"[Transient] Falha no teste: {ex.Message}");
            }
            finally
            {
                await driver.DisconnectAsync();
            }
        }

        // Sink
        public class UiSink : ILogEventSink
        {
            private readonly Action<LogEvent> _action;
            public UiSink(Action<LogEvent> action) => _action = action;
            public void Emit(LogEvent logEvent) => _action(logEvent);
        }

        private void UpdateConfigFieldsState()
        {
            if (_configFields == null) return;

            // Regra de Mínimo e Máximo
            if (_configFields.Count <= 1)
            {
                foreach (var item in _configFields)
                {
                    item.RemoveButtonVisibility = Visibility.Collapsed;
                }
            }
            else
            {
                foreach (var item in _configFields)
                {
                    item.RemoveButtonVisibility = Visibility.Visible;
                }
            }

            // Regra de Máximo (3)
            if (_configFields.Count >= 3)
            {
                if (BtnAddConfig != null)
                {
                    BtnAddConfig.Visibility = Visibility.Collapsed;
                    BtnAddConfig.IsEnabled = false;
                }
            }
            else
            {
                if (BtnAddConfig != null)
                {
                    BtnAddConfig.Visibility = Visibility.Visible;
                    BtnAddConfig.IsEnabled = true;
                }
            }
            
            // Atualiza visibilidade S7 de forma responsiva (Sprint 3)
            UpdateS7PanelVisibility();
        }

        private void UpdateS7PanelVisibility()
        {
            if (_configFields == null || PnlSiemensDbConfig == null) return;

            bool hasBool = _configFields.Any(f => f.FieldType == "Boolean");
            bool hasNumeric = _configFields.Any(f => f.FieldType == "Numeric");
            bool hasString = _configFields.Any(f => f.FieldType == "String");

            if (GrpDbBool != null)
                GrpDbBool.Visibility = hasBool ? Visibility.Visible : Visibility.Collapsed;

            if (GrpDbInt != null)
                GrpDbInt.Visibility = hasNumeric ? Visibility.Visible : Visibility.Collapsed;

            if (GrpDbPartNumber != null)
                GrpDbPartNumber.Visibility = hasString ? Visibility.Visible : Visibility.Collapsed;

            if (GrpDbStatus != null)
                GrpDbStatus.Visibility = Visibility.Visible;

            // Recalcula colunas do UniformGrid com base nos itens ativos e largura atual
            if (PnlSiemensDbConfig is System.Windows.Controls.Primitives.UniformGrid grid)
            {
                double width = grid.ActualWidth;
                if (width > 0)
                {
                    int visibleCount = 0;
                    if (hasBool) visibleCount++;
                    if (hasNumeric) visibleCount++;
                    if (hasString) visibleCount++;
                    visibleCount++; // GrpDbStatus está sempre visível
                    
                    int cols = (int)(width / 140);
                    if (cols < 1) cols = 1;
                    if (cols > visibleCount) cols = visibleCount;
                    grid.Columns = cols;
                }
            }
        }

        private void PnlSiemensDbConfig_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (PnlSiemensDbConfig is System.Windows.Controls.Primitives.UniformGrid grid)
            {
                double width = e.NewSize.Width;
                
                int visibleCount = 0;
                if (GrpDbBool != null && GrpDbBool.Visibility == Visibility.Visible) visibleCount++;
                if (GrpDbInt != null && GrpDbInt.Visibility == Visibility.Visible) visibleCount++;
                if (GrpDbPartNumber != null && GrpDbPartNumber.Visibility == Visibility.Visible) visibleCount++;
                if (GrpDbStatus != null && GrpDbStatus.Visibility == Visibility.Visible) visibleCount++;
                
                if (visibleCount == 0) return;
                
                int cols = (int)(width / 140);
                if (cols < 1) cols = 1;
                if (cols > visibleCount) cols = visibleCount;
                
                grid.Columns = cols;
            }
        }

        private void BtnAddConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_configFields != null && _configFields.Count < 3)
            {
                if (TxtInlineWarning != null)
                {
                    TxtInlineWarning.Text = "";
                    TxtInlineWarning.Visibility = Visibility.Collapsed;
                }

                // Encontra o primeiro tipo que não está em uso
                var usedTypes = _configFields.Select(f => f.FieldType).ToList();
                var allTypes = new[] { "Boolean", "Numeric", "String" };
                var unusedType = allTypes.FirstOrDefault(t => !usedTypes.Contains(t)) ?? "Boolean";

                _configFields.Add(new ConfigFieldItem { FieldType = unusedType });
                UpdateConfigFieldsState();
            }
        }

        private void BtnRemoveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ConfigFieldItem item && _configFields != null)
            {
                if (TxtInlineWarning != null)
                {
                    TxtInlineWarning.Text = "";
                    TxtInlineWarning.Visibility = Visibility.Collapsed;
                }

                _configFields.Remove(item);
                UpdateConfigFieldsState();
            }
        }

        private bool _isUpdatingFieldTypes = false;

        private void CmbFieldType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtInlineWarning != null)
            {
                TxtInlineWarning.Text = "";
                TxtInlineWarning.Visibility = Visibility.Collapsed;
            }

            if (_isUpdatingFieldTypes) return;

            if (sender is ComboBox cb && cb.DataContext is ConfigFieldItem currentItem && _configFields != null)
            {
                string newType = cb.SelectedValue?.ToString() ?? "Boolean";

                // Encontra se já existe outro item com esse mesmo tipo
                var duplicateItem = _configFields.FirstOrDefault(item => item != currentItem && item.FieldType == newType);
                if (duplicateItem != null)
                {
                    string? oldType = null;
                    if (e.RemovedItems.Count > 0)
                    {
                        if (e.RemovedItems[0] is ComboBoxItem cbi)
                            oldType = cbi.Tag?.ToString();
                        else if (e.RemovedItems[0] is string str)
                            oldType = str;
                    }

                    if (string.IsNullOrEmpty(oldType))
                    {
                        var allTypes = new[] { "Boolean", "Numeric", "String" };
                        var usedTypes = _configFields.Select(item => item.FieldType).ToList();
                        oldType = allTypes.FirstOrDefault(t => !usedTypes.Contains(t)) ?? "Boolean";
                    }

                    _isUpdatingFieldTypes = true;
                    try
                    {
                        duplicateItem.FieldType = oldType;
                    }
                    finally
                    {
                        _isUpdatingFieldTypes = false;
                    }
                }
            }

            UpdateConfigFieldsState();
        }

        private bool ContainsTemplateTag(string template, string tagName)
        {
            if (string.IsNullOrEmpty(template)) return false;
            string pattern = @"\{\{\s*" + Regex.Escape(tagName) + @"\s*\}\}";
            return Regex.IsMatch(template, pattern, RegexOptions.IgnoreCase);
        }

        public class ConfigFieldItem : System.ComponentModel.INotifyPropertyChanged
        {
            private string _fieldType = "Boolean";
            public string FieldType
            {
                get => _fieldType;
                set
                {
                    if (_fieldType != value)
                    {
                        _fieldType = value;
                        OnPropertyChanged(nameof(FieldType));
                    }
                }
            }

            private Visibility _removeButtonVisibility = Visibility.Collapsed;
            public Visibility RemoveButtonVisibility
            {
                get => _removeButtonVisibility;
                set
                {
                    if (_removeButtonVisibility != value)
                    {
                        _removeButtonVisibility = value;
                        OnPropertyChanged(nameof(RemoveButtonVisibility));
                    }
                }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
