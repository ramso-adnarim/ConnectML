using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConnectML.UI
{
    /// <summary>
    /// Janela de Overlay HUD do ConnectML.
    /// Exibida em tela cheia de forma transparente sem interceptar cliques do usuário no centro da tela.
    /// Suporta animações de estado (Aguardando / Concluído) e arraste com snap magnético nos 4 cantos.
    /// </summary>
    public partial class OverlayWidgetWindow : Window
    {
        // Evento disparado quando o usuário clica no botão para restaurar a janela principal
        public event EventHandler? RequestRestoreMainWindow;

        // Evento disparado quando a posição de acoplamento da aba é alterada
        public event EventHandler<string>? SnapPositionChanged;

        // Evento disparado quando as configurações do HUD são alteradas ou salvas pela janela de configurações
        public event EventHandler<OverlaySettingsChangedEventArgs>? OverlaySettingsPersisted;

        public string CurrentSnapPosition => _currentSnapPosition;
        public int BorderThicknessValue => _borderThickness;
        public double FontSizeValue => _fontSize;
        public int HoldSecondsValue => _holdSeconds;
        private string _currentSnapPosition = "Top";
        private int _borderThickness = 3;
        private double _fontSize = 13;
        private int _holdSeconds = 10;
        private OverlaySettingsWindow? _settingsDialog;
        private Storyboard? _pulseStoryboard;

        // Dwell Timer Conjugado (v1.3.0)
        private bool _plcResetReceived = false;
        private bool _dwellTimeElapsed = false;
        private readonly object _stateLock = new object();
        private CancellationTokenSource? _dwellCts;

        // Controle de Arraste (Drag & Drop)
        private bool _isDragging = false;
        private Point _dragStartMousePos;
        private double _dragStartTranslateX;
        private double _dragStartTranslateY;

        // Cores da Identidade Visual
        private static readonly Brush YellowBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCC00"));
        private static readonly Brush GreenBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        private static readonly Brush GrayBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        private bool _isStateInitialized = false;

        #region Win32 Non-Activating Styles

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        #endregion

        public OverlayWidgetWindow()
        {
            InitializeComponent();
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Loaded += OverlayWidgetWindow_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Aplica estilos de janela Win32:
            // WS_EX_NOACTIVATE: Impede que o clique na aba roube o foco de digitação do MeasurLink
            // WS_EX_TOOLWINDOW: Oculta a janela de alternadores como Alt+Tab
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        private void OverlayWidgetWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Garante que o Overlay preencha toda a área da tela do monitor principal
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            _pulseStoryboard = (Storyboard)FindResource("PulseStoryboard");

            // Inicia no estado apropriado (se ainda não definido pela janela chamadora)
            if (!_isStateInitialized)
            {
                SetWaitingState();
            }
            UpdateBorderResizeStrips();
        }

        #region Máquina de Estados Visuais (UX)

        /// <summary>
        /// Define o estado visual para 'Aguardando Medição':
        /// Cores em Amarelo Âmbar e animação cíclica de pulso ativa.
        /// </summary>
        public void SetWaitingState()
        {
            if (Dispatcher.HasShutdownStarted) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _isStateInitialized = true;
                        TxtOverlayStatus.Text = "AGUARDANDO MEDIÇÃO";

                        OverlayBorder.BorderBrush = YellowBrush;
                        OverlayTabContainer.BorderBrush = YellowBrush;
                        StateDot.Fill = YellowBrush;

                        _pulseStoryboard?.Begin(this, isControllable: true);
                    }
                    catch (Exception) { }
                });
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Define o estado visual para 'Medição Concluída':
        /// Interrompe a animação de pulso e fixa as bordas em Verde Esmeralda Sólido.
        /// </summary>
        public void SetCompletedState(string? message = null)
        {
            if (Dispatcher.HasShutdownStarted) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _isStateInitialized = true;
                        _pulseStoryboard?.Stop(this);

                        OverlayBorder.Opacity = 1.0;
                        StateDot.Opacity = 1.0;

                        OverlayBorder.BorderBrush = GreenBrush;
                        OverlayTabContainer.BorderBrush = GreenBrush;
                        StateDot.Fill = GreenBrush;

                        TxtOverlayStatus.Text = !string.IsNullOrWhiteSpace(message) ? message : "CONCLUÍDO";
                    }
                    catch (Exception) { }
                });
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Define o estado visual para 'Processamento Interrompido':
        /// Interrompe a animação de pulso e fixa as bordas e indicador em Cinza Sólido (#64748B).
        /// </summary>
        public void SetInterruptedState()
        {
            if (Dispatcher.HasShutdownStarted) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        lock (_stateLock)
                        {
                            _dwellCts?.Cancel();
                            _dwellCts?.Dispose();
                            _dwellCts = null;
                            _plcResetReceived = false;
                            _dwellTimeElapsed = false;
                        }

                        _isStateInitialized = true;
                        _pulseStoryboard?.Stop(this);

                        OverlayBorder.Opacity = 1.0;
                        StateDot.Opacity = 1.0;

                        OverlayBorder.BorderBrush = GrayBrush;
                        OverlayTabContainer.BorderBrush = GrayBrush;
                        StateDot.Fill = GrayBrush;

                        TxtOverlayStatus.Text = "PROCESSAMENTO INTERROMPIDO";
                    }
                    catch (Exception) { }
                });
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Disparado quando um arquivo de medição é processado e despachado.
        /// Fixa o estado em 'Medição Concluída' e inicia a contagem do tempo mínimo de tela (holdSeconds).
        /// </summary>
        public void TriggerMeasurementCompleted(int holdSeconds, string? message = null)
        {
            lock (_stateLock)
            {
                _plcResetReceived = false;
                _dwellTimeElapsed = false;

                SetCompletedState(message);

                _dwellCts?.Cancel();
                _dwellCts?.Dispose();
                _dwellCts = new CancellationTokenSource();
                var token = _dwellCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        int delayMs = Math.Clamp(holdSeconds, 1, 30) * 1000;
                        await Task.Delay(delayMs, token);

                        lock (_stateLock)
                        {
                            _dwellTimeElapsed = true;
                            EvaluateTransitionToWaiting();
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }
                }, token);
            }
        }

        /// <summary>
        /// Notifica que o PLC confirmou o recebimento e resetou a variável de status para FALSE.
        /// </summary>
        public void NotifyPlcResetConfirmed()
        {
            lock (_stateLock)
            {
                _plcResetReceived = true;
                EvaluateTransitionToWaiting();
            }
        }

        private void EvaluateTransitionToWaiting()
        {
            // Regra Conjugada: Só retorna para 'Aguardando Medição' se:
            // 1. O PLC resetou para FALSE (_plcResetReceived) E
            // 2. O tempo mínimo de tela já passou (_dwellTimeElapsed)
            if (_plcResetReceived && _dwellTimeElapsed)
            {
                SetWaitingState();
            }
        }

        /// <summary>
        /// Reseta o ciclo de vida do overlay para 'Aguardando' limpo (ou 'Interrompido' se toWaiting = false).
        /// </summary>
        public void ResetLifecycleState(bool toWaiting = true)
        {
            lock (_stateLock)
            {
                _dwellCts?.Cancel();
                _dwellCts?.Dispose();
                _dwellCts = null;
                _plcResetReceived = false;
                _dwellTimeElapsed = false;
                if (toWaiting)
                    SetWaitingState();
                else
                    SetInterruptedState();
            }
        }

        #endregion

        #region Configurações de Borda, Escala e Snap

        /// <summary>
        /// Aplica todas as configurações carregadas do AppConfig.
        /// </summary>
        public void ApplySettings(int borderThickness, double fontSize, int holdSeconds, string snapPosition)
        {
            _holdSeconds = Math.Clamp(holdSeconds, 1, 30);
            SetBorderThickness(borderThickness);
            SetFontSize(fontSize);
            SetSnapPosition(snapPosition);
        }

        /// <summary>
        /// Atualiza dinamicamente a espessura da borda perimetral luminosa e compensa a margem da aba e das tiras de redimensionamento.
        /// Limite expandido: 1 a 60 px para alta visibilidade industrial.
        /// </summary>
        public void SetBorderThickness(int thickness)
        {
            _borderThickness = Math.Clamp(thickness, 1, 60);
            OverlayBorder.BorderThickness = new Thickness(_borderThickness);
            UpdateTabMargin();
            UpdateBorderResizeStrips();
        }

        /// <summary>
        /// Atualiza dinamicamente o posicionamento das tiras de redimensionamento da borda perimetral.
        /// </summary>
        private void UpdateBorderResizeStrips()
        {
            Dispatcher.Invoke(() =>
            {
                if (BorderResizeTop == null || BorderResizeBottom == null || BorderResizeLeft == null || BorderResizeRight == null) return;

                // Faixa estreita de 8px (±4px) na aresta divisória da borda
                double offset = Math.Max(0, _borderThickness - 4);
                BorderResizeTop.Height = 8;
                BorderResizeTop.Margin = new Thickness(0, offset, 0, 0);

                BorderResizeBottom.Height = 8;
                BorderResizeBottom.Margin = new Thickness(0, 0, 0, offset);

                BorderResizeLeft.Width = 8;
                BorderResizeLeft.Margin = new Thickness(offset, 0, 0, 0);

                BorderResizeRight.Width = 8;
                BorderResizeRight.Margin = new Thickness(0, 0, offset, 0);
            });
        }

        /// <summary>
        /// Atualiza dinamicamente a escala da fonte e o tamanho de todos os elementos da aba.
        /// Limite expandido: 11 a 60 pt para conforto e legibilidade a longas distâncias da tela.
        /// </summary>
        public void SetFontSize(double size)
        {
            _fontSize = Math.Clamp(size, 11, 60);

            Dispatcher.Invoke(() =>
            {
                TxtOverlayStatus.FontSize = _fontSize;

                // 1. Elementos que DEVEM crescer com a fonte (título e indicador circular)
                double dotSize = Math.Max(8, Math.Round(_fontSize * 0.65));
                StateDot.Width = dotSize;
                StateDot.Height = dotSize;

                // 2. Controles operacionais com tamanho base estável (~14px) e redução responsiva suave se _fontSize < 14
                double iconSettingsSize = _fontSize >= 14 ? 14 : Math.Clamp(Math.Round(_fontSize * 0.95), 9, 14);
                IconSettingsViewbox.Width = iconSettingsSize;
                IconSettingsViewbox.Height = iconSettingsSize;

                double iconRestoreSize = _fontSize >= 14 ? 13 : Math.Clamp(Math.Round(_fontSize * 0.92), 9, 13);
                IconRestoreViewbox.Width = iconRestoreSize;
                IconRestoreViewbox.Height = iconRestoreSize;

                TxtBtnRestore.FontSize = _fontSize >= 14 ? 11 : Math.Clamp(_fontSize - 2, 8, 11);

                // Manipulador de arraste (GripHandle - pontos verticais de orientação de movimento)
                double gripW = _fontSize >= 14 ? 10 : Math.Clamp(Math.Round(_fontSize * 0.70), 7, 10);
                double gripH = _fontSize >= 14 ? 14 : Math.Clamp(Math.Round(_fontSize * 1.00), 10, 14);
                GripViewbox.Width = gripW;
                GripViewbox.Height = gripH;

                // Manipulador de redimensionamento (Corner Grip no canto)
                double resizeGripSize = _fontSize >= 14 ? 12 : Math.Clamp(Math.Round(_fontSize * 0.85), 9, 12);
                if (ResizeGripViewbox != null)
                {
                    ResizeGripViewbox.Width = resizeGripSize;
                    ResizeGripViewbox.Height = resizeGripSize;
                }

                // 3. Espaçamento respirável entre título e controles, e aproximação dos botões da borda direita da aba
                double padH = Math.Round(_fontSize * 0.85);
                double padV = Math.Round(_fontSize * 0.40);
                double rightMargin = Math.Clamp(Math.Round(_fontSize * 0.35), 8, 14);
                double bottomMargin = Math.Clamp(Math.Round(_fontSize * 0.35), 6, 12);

                OverlayTabContainer.Padding = new Thickness(0);

                // Distanciamento respirável do título para os controles (não colado no texto)
                double titleGap = Math.Max(20, Math.Round(_fontSize * 0.85));

                if (TabContentPanel.Orientation == Orientation.Horizontal)
                {
                    TabContentPanel.Margin = new Thickness(padH, padV, rightMargin, padV);

                    // Separadores com altura ergonômica estável (~14px)
                    double sepHeight = _fontSize >= 14 ? 14 : Math.Max(9, Math.Round(_fontSize * 0.9));
                    TabSeparator1.Height = sepHeight;
                    TabSeparator1.Width = 1;
                    TabSeparator1.Margin = new Thickness(titleGap, 0, 6, 0);

                    TabSeparator2.Height = sepHeight;
                    TabSeparator2.Width = 1;
                    TabSeparator2.Margin = new Thickness(6, 0, 6, 0);

                    if (TabSeparator3 != null) TabSeparator3.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TabContentPanel.Margin = new Thickness(padV, padH, padV, bottomMargin);

                    // Separadores verticais com largura ergonômica estável (~14px)
                    double sepWidth = _fontSize >= 14 ? 14 : Math.Max(9, Math.Round(_fontSize * 0.9));
                    TabSeparator1.Width = sepWidth;
                    TabSeparator1.Height = 1;
                    TabSeparator1.Margin = new Thickness(0, titleGap, 0, 6);

                    TabSeparator2.Width = sepWidth;
                    TabSeparator2.Height = 1;
                    TabSeparator2.Margin = new Thickness(0, 6, 0, 6);

                    if (TabSeparator3 != null) TabSeparator3.Visibility = Visibility.Collapsed;
                }
            });
        }

        /// <summary>
        /// Compensa a margem da aba com base na espessura da borda perimetral ativa.
        /// </summary>
        private void UpdateTabMargin()
        {
            Dispatcher.Invoke(() =>
            {
                switch (_currentSnapPosition)
                {
                    case "BOTTOM":
                        OverlayTabContainer.Margin = new Thickness(0, 0, 0, _borderThickness);
                        break;
                    case "LEFT":
                        OverlayTabContainer.Margin = new Thickness(_borderThickness, 0, 0, 0);
                        break;
                    case "RIGHT":
                        OverlayTabContainer.Margin = new Thickness(0, 0, _borderThickness, 0);
                        break;
                    case "TOP":
                    default:
                        OverlayTabContainer.Margin = new Thickness(0, _borderThickness, 0, 0);
                        break;
                }
            });
        }

        /// <summary>
        /// Atualiza o posicionamento, layout e formato da aba de ancoragem.
        /// </summary>
        public void SetSnapPosition(string position)
        {
            _currentSnapPosition = position.ToUpperInvariant();

            Dispatcher.Invoke(() =>
            {
                double sepHeight = Math.Round(_fontSize * 1.1);
                double sepWidth = Math.Round(_fontSize * 1.3);

                switch (_currentSnapPosition)
                {
                    case "BOTTOM":
                        OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Center;
                        OverlayTabContainer.VerticalAlignment = VerticalAlignment.Bottom;
                        OverlayTabContainer.CornerRadius = new CornerRadius(8, 8, 0, 0);
                        OverlayTabContainer.BorderThickness = new Thickness(1, 1, 1, 0);
                        TabContentPanel.Orientation = Orientation.Horizontal;

                        TextRotateTransform.Angle = 0;
                        TxtOverlayStatus.Margin = new Thickness(0);
                        TxtOverlayStatus.TextAlignment = TextAlignment.Left;

                        TabSeparator1.Width = 1; TabSeparator1.Height = sepHeight;
                        TabSeparator1.Margin = new Thickness(10, 0, 8, 0);
                        TabSeparator2.Width = 1; TabSeparator2.Height = sepHeight;
                        TabSeparator2.Margin = new Thickness(6, 0, 8, 0);
                        if (TabSeparator3 != null) TabSeparator3.Visibility = Visibility.Collapsed;

                        StateDot.Margin = new Thickness(0, 0, 8, 0);
                        GripHandle.Margin = new Thickness(0, 0, 8, 0);
                        if (TabResizeGrip != null)
                        {
                            TabResizeGrip.HorizontalAlignment = HorizontalAlignment.Right;
                            TabResizeGrip.VerticalAlignment = VerticalAlignment.Top;
                            TabResizeGrip.Margin = new Thickness(0, 3, 3, 0);
                            TabResizeGrip.Cursor = Cursors.SizeNESW;
                        }
                        if (TabEdgeResizeStrip != null)
                        {
                            TabEdgeResizeStrip.VerticalAlignment = VerticalAlignment.Top;
                            TabEdgeResizeStrip.HorizontalAlignment = HorizontalAlignment.Stretch;
                            TabEdgeResizeStrip.Height = 10;
                            TabEdgeResizeStrip.Width = double.NaN;
                            TabEdgeResizeStrip.Cursor = Cursors.SizeNS;
                            TabEdgeResizeStrip.Margin = new Thickness(0, -4, 0, 0);
                        }
                        TxtBtnRestore.Visibility = Visibility.Visible;
                        break;

                    case "LEFT":
                        OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Left;
                        OverlayTabContainer.VerticalAlignment = VerticalAlignment.Center;
                        OverlayTabContainer.CornerRadius = new CornerRadius(0, 8, 8, 0);
                        OverlayTabContainer.BorderThickness = new Thickness(0, 1, 1, 1);
                        TabContentPanel.Orientation = Orientation.Vertical;

                        TextRotateTransform.Angle = 90;
                        TxtOverlayStatus.Margin = new Thickness(0, 8, 0, 8);
                        TxtOverlayStatus.TextAlignment = TextAlignment.Center;

                        TabSeparator1.Width = sepWidth; TabSeparator1.Height = 1;
                        TabSeparator1.Margin = new Thickness(0, 8, 0, 8);
                        TabSeparator2.Width = sepWidth; TabSeparator2.Height = 1;
                        TabSeparator2.Margin = new Thickness(0, 6, 0, 8);
                        if (TabSeparator3 != null) TabSeparator3.Visibility = Visibility.Collapsed;

                        StateDot.Margin = new Thickness(0, 0, 0, 6);
                        GripHandle.Margin = new Thickness(0, 0, 0, 8);
                        if (TabResizeGrip != null)
                        {
                            TabResizeGrip.HorizontalAlignment = HorizontalAlignment.Right;
                            TabResizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
                            TabResizeGrip.Margin = new Thickness(0, 0, 3, 3);
                            TabResizeGrip.Cursor = Cursors.SizeNWSE;
                        }
                        if (TabEdgeResizeStrip != null)
                        {
                            TabEdgeResizeStrip.VerticalAlignment = VerticalAlignment.Stretch;
                            TabEdgeResizeStrip.HorizontalAlignment = HorizontalAlignment.Right;
                            TabEdgeResizeStrip.Width = 10;
                            TabEdgeResizeStrip.Height = double.NaN;
                            TabEdgeResizeStrip.Cursor = Cursors.SizeWE;
                            TabEdgeResizeStrip.Margin = new Thickness(0, 0, -4, 0);
                        }
                        TxtBtnRestore.Visibility = Visibility.Collapsed;
                        break;

                    case "RIGHT":
                        OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Right;
                        OverlayTabContainer.VerticalAlignment = VerticalAlignment.Center;
                        OverlayTabContainer.CornerRadius = new CornerRadius(8, 0, 0, 8);
                        OverlayTabContainer.BorderThickness = new Thickness(1, 1, 0, 1);
                        TabContentPanel.Orientation = Orientation.Vertical;

                        TextRotateTransform.Angle = -90;
                        TxtOverlayStatus.Margin = new Thickness(0, 8, 0, 8);
                        TxtOverlayStatus.TextAlignment = TextAlignment.Center;

                        TabSeparator1.Width = sepWidth; TabSeparator1.Height = 1;
                        TabSeparator1.Margin = new Thickness(0, 8, 0, 8);
                        TabSeparator2.Width = sepWidth; TabSeparator2.Height = 1;
                        TabSeparator2.Margin = new Thickness(0, 6, 0, 8);
                        if (TabSeparator3 != null) TabSeparator3.Visibility = Visibility.Collapsed;

                        StateDot.Margin = new Thickness(0, 0, 0, 6);
                        GripHandle.Margin = new Thickness(0, 0, 0, 8);
                        if (TabResizeGrip != null)
                        {
                            TabResizeGrip.HorizontalAlignment = HorizontalAlignment.Left;
                            TabResizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
                            TabResizeGrip.Margin = new Thickness(3, 0, 0, 3);
                            TabResizeGrip.Cursor = Cursors.SizeNESW;
                        }
                        if (TabEdgeResizeStrip != null)
                        {
                            TabEdgeResizeStrip.VerticalAlignment = VerticalAlignment.Stretch;
                            TabEdgeResizeStrip.HorizontalAlignment = HorizontalAlignment.Left;
                            TabEdgeResizeStrip.Width = 10;
                            TabEdgeResizeStrip.Height = double.NaN;
                            TabEdgeResizeStrip.Cursor = Cursors.SizeWE;
                            TabEdgeResizeStrip.Margin = new Thickness(-4, 0, 0, 0);
                        }
                        TxtBtnRestore.Visibility = Visibility.Collapsed;
                        break;

                    case "TOP":
                    default:
                        _currentSnapPosition = "TOP";
                        OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Center;
                        OverlayTabContainer.VerticalAlignment = VerticalAlignment.Top;
                        OverlayTabContainer.CornerRadius = new CornerRadius(0, 0, 8, 8);
                        OverlayTabContainer.BorderThickness = new Thickness(1, 0, 1, 1);
                        TabContentPanel.Orientation = Orientation.Horizontal;

                        TextRotateTransform.Angle = 0;
                        TxtOverlayStatus.Margin = new Thickness(0);
                        TxtOverlayStatus.TextAlignment = TextAlignment.Left;

                        TabSeparator1.Width = 1; TabSeparator1.Height = sepHeight;
                        TabSeparator1.Margin = new Thickness(10, 0, 8, 0);
                        TabSeparator2.Width = 1; TabSeparator2.Height = sepHeight;
                        TabSeparator2.Margin = new Thickness(6, 0, 8, 0);
                        if (TabSeparator3 != null) TabSeparator3.Visibility = Visibility.Collapsed;

                        StateDot.Margin = new Thickness(0, 0, 8, 0);
                        GripHandle.Margin = new Thickness(0, 0, 8, 0);
                        if (TabResizeGrip != null)
                        {
                            TabResizeGrip.HorizontalAlignment = HorizontalAlignment.Right;
                            TabResizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
                            TabResizeGrip.Margin = new Thickness(0, 0, 3, 3);
                            TabResizeGrip.Cursor = Cursors.SizeNWSE;
                        }
                        if (TabEdgeResizeStrip != null)
                        {
                            TabEdgeResizeStrip.VerticalAlignment = VerticalAlignment.Bottom;
                            TabEdgeResizeStrip.HorizontalAlignment = HorizontalAlignment.Stretch;
                            TabEdgeResizeStrip.Height = 10;
                            TabEdgeResizeStrip.Width = double.NaN;
                            TabEdgeResizeStrip.Cursor = Cursors.SizeNS;
                            TabEdgeResizeStrip.Margin = new Thickness(0, 0, 0, -4);
                        }
                        TxtBtnRestore.Visibility = Visibility.Visible;
                        break;
                }

                UpdateTabMargin();
                SetFontSize(_fontSize);
                SnapPositionChanged?.Invoke(this, _currentSnapPosition);
            });
        }

        #endregion

        #region Redimensionamento da Borda via Mouse Drag

        private bool _isResizingBorder = false;
        private string _activeBorderSide = "TOP";
        private Point _borderResizeStartPos;
        private int _borderResizeStartThickness;

        private void BorderResize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string side)
            {
                _isResizingBorder = true;
                _activeBorderSide = side.ToUpperInvariant();
                _borderResizeStartPos = e.GetPosition(this);
                _borderResizeStartThickness = _borderThickness;
                elem.CaptureMouse();
                e.Handled = true;
            }
        }

        private void BorderResize_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizingBorder) return;
            Point currentPos = e.GetPosition(this);
            int delta = 0;

            switch (_activeBorderSide)
            {
                case "TOP":
                    delta = (int)(currentPos.Y - _borderResizeStartPos.Y);
                    break;
                case "BOTTOM":
                    delta = (int)(_borderResizeStartPos.Y - currentPos.Y);
                    break;
                case "LEFT":
                    delta = (int)(currentPos.X - _borderResizeStartPos.X);
                    break;
                case "RIGHT":
                    delta = (int)(_borderResizeStartPos.X - currentPos.X);
                    break;
            }

            int newThickness = Math.Clamp(_borderResizeStartThickness + delta, 1, 60);
            if (newThickness != _borderThickness)
            {
                SetBorderThickness(newThickness);
            }
        }

        private void BorderResize_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isResizingBorder) return;
            _isResizingBorder = false;
            if (sender is FrameworkElement elem)
            {
                elem.ReleaseMouseCapture();
            }
            OverlaySettingsPersisted?.Invoke(this, GetCurrentSettings());
            e.Handled = true;
        }

        #endregion

        #region Redimensionamento da Aba via Mouse Drag

        private bool _isResizingTab = false;
        private Point _tabResizeStartPos;
        private double _tabResizeStartFontSize;

        private void TabResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isResizingTab = true;
            _tabResizeStartPos = e.GetPosition(this);
            _tabResizeStartFontSize = _fontSize;
            if (sender is FrameworkElement elem)
            {
                elem.CaptureMouse();
            }
            e.Handled = true;
        }

        private void TabResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizingTab) return;
            Point currentPos = e.GetPosition(this);

            double delta;
            switch (_currentSnapPosition)
            {
                case "TOP":
                    delta = (currentPos.Y - _tabResizeStartPos.Y) * 0.35 + (currentPos.X - _tabResizeStartPos.X) * 0.15;
                    break;
                case "BOTTOM":
                    delta = (_tabResizeStartPos.Y - currentPos.Y) * 0.35 + (currentPos.X - _tabResizeStartPos.X) * 0.15;
                    break;
                case "LEFT":
                    delta = (currentPos.X - _tabResizeStartPos.X) * 0.35 + (currentPos.Y - _tabResizeStartPos.Y) * 0.15;
                    break;
                case "RIGHT":
                    delta = (_tabResizeStartPos.X - currentPos.X) * 0.35 + (currentPos.Y - _tabResizeStartPos.Y) * 0.15;
                    break;
                default:
                    delta = (currentPos.Y - _tabResizeStartPos.Y) * 0.35;
                    break;
            }

            double newSize = Math.Clamp(Math.Round(_tabResizeStartFontSize + delta), 11, 60);
            if (Math.Abs(newSize - _fontSize) >= 1)
            {
                SetFontSize(newSize);
            }
        }

        private void TabResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isResizingTab) return;
            _isResizingTab = false;
            if (sender is FrameworkElement elem)
            {
                elem.ReleaseMouseCapture();
            }
            OverlaySettingsPersisted?.Invoke(this, GetCurrentSettings());
            e.Handled = true;
        }

        private void TabResizeGrip_MouseEnter(object sender, MouseEventArgs e)
        {
            if (PathResizeGrip != null)
                PathResizeGrip.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
        }

        private void TabResizeGrip_MouseLeave(object sender, MouseEventArgs e)
        {
            if (PathResizeGrip != null)
                PathResizeGrip.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        }

        public OverlaySettingsChangedEventArgs GetCurrentSettings()
        {
            return new OverlaySettingsChangedEventArgs
            {
                BorderThickness = _borderThickness,
                FontSize = _fontSize,
                HoldSeconds = _holdSeconds,
                SnapPosition = _currentSnapPosition
            };
        }

        #endregion

        #region Drag & Drop com Snap Magnético

        private void Btn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Impede que cliques nos botões internos disparem o arrasto da aba
            e.Handled = false;
        }

        private void OverlayTabContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Se o clique foi em qualquer botão da aba ou em controles de redimensionamento, não inicia o arraste de reposicionamento
            if (e.OriginalSource is DependencyObject dep)
            {
                if (FindParent<Button>(dep) != null) return;
                if (dep == TabResizeGrip || FindParent<FrameworkElement>(dep) == TabResizeGrip) return;
                if (dep == TabEdgeResizeStrip) return;
            }

            _isDragging = true;
            _dragStartMousePos = e.GetPosition(this);
            _dragStartTranslateX = TabTranslateTransform.X;
            _dragStartTranslateY = TabTranslateTransform.Y;

            OverlayTabContainer.CaptureMouse();
            OverlayTabContainer.Opacity = 0.85;
        }

        private void OverlayTabContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point currentPos = e.GetPosition(this);
            double deltaX = currentPos.X - _dragStartMousePos.X;
            double deltaY = currentPos.Y - _dragStartMousePos.Y;

            TabTranslateTransform.X = _dragStartTranslateX + deltaX;
            TabTranslateTransform.Y = _dragStartTranslateY + deltaY;
        }

        private void OverlayTabContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            OverlayTabContainer.ReleaseMouseCapture();
            OverlayTabContainer.Opacity = 1.0;

            try
            {
                // Calcula a posição absoluta da aba dentro da tela do Overlay
                GeneralTransform transform = OverlayTabContainer.TransformToAncestor(this);
                Rect tabBounds = transform.TransformBounds(new Rect(0, 0, OverlayTabContainer.ActualWidth, OverlayTabContainer.ActualHeight));

                double distTop = tabBounds.Top;
                double distBottom = ActualHeight - tabBounds.Bottom;
                double distLeft = tabBounds.Left;
                double distRight = ActualWidth - tabBounds.Right;

                // Determina a borda mais próxima (Snap To Edge)
                string bestSide = "TOP";
                double minDist = distTop;

                if (distBottom < minDist)
                {
                    minDist = distBottom;
                    bestSide = "BOTTOM";
                }
                if (distLeft < minDist)
                {
                    minDist = distLeft;
                    bestSide = "LEFT";
                }
                if (distRight < minDist)
                {
                    bestSide = "RIGHT";
                }

                // Reseta a transformação de translação manual para encaixe limpo
                TabTranslateTransform.X = 0;
                TabTranslateTransform.Y = 0;

                SetSnapPosition(bestSide);
            }
            catch (Exception)
            {
                TabTranslateTransform.X = 0;
                TabTranslateTransform.Y = 0;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }

        #endregion

        #region Botões de Ação da Aba (Configurações e Restaurar)

        private void BtnOverlaySettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsDialog != null && _settingsDialog.IsVisible)
            {
                _settingsDialog.Activate();
                return;
            }

            _settingsDialog = new OverlaySettingsWindow(this, 
                                                        _borderThickness, 
                                                        _fontSize, 
                                                        _holdSeconds, 
                                                        _currentSnapPosition);
            _settingsDialog.Owner = this;

            _settingsDialog.SettingsChanged += (s, args) =>
            {
                _borderThickness = args.BorderThickness;
                _fontSize = args.FontSize;
                _holdSeconds = args.HoldSeconds;
                OverlaySettingsPersisted?.Invoke(this, args);
            };

            _settingsDialog.SettingsSaved += (s, args) =>
            {
                _borderThickness = args.BorderThickness;
                _fontSize = args.FontSize;
                _holdSeconds = args.HoldSeconds;
                OverlaySettingsPersisted?.Invoke(this, args);
            };

            _settingsDialog.Closed += (s, args) => _settingsDialog = null;
            _settingsDialog.Show();
        }

        private void BtnRestoreMainWindow_Click(object sender, RoutedEventArgs e)
        {
            RequestRestoreMainWindow?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
