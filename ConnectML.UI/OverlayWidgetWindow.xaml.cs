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

            // Inicia em modo de espera (Aguardando Medição)
            SetWaitingState();
            UpdateBorderResizeStrips();
        }

        #region Máquina de Estados Visuais (UX)

        /// <summary>
        /// Define o estado visual para 'Aguardando Medição':
        /// Cores em Amarelo Âmbar e animação cíclica de pulso ativa.
        /// </summary>
        public void SetWaitingState()
        {
            Dispatcher.Invoke(() =>
            {
                TxtOverlayStatus.Text = "Aguardando Medição";

                OverlayBorder.BorderBrush = YellowBrush;
                OverlayTabContainer.BorderBrush = YellowBrush;
                StateDot.Fill = YellowBrush;

                _pulseStoryboard?.Begin(this, isControllable: true);
            });
        }

        /// <summary>
        /// Define o estado visual para 'Medição Concluída':
        /// Interrompe a animação de pulso e fixa as bordas em Verde Esmeralda Sólido.
        /// </summary>
        public void SetCompletedState(string? message = null)
        {
            Dispatcher.Invoke(() =>
            {
                _pulseStoryboard?.Stop(this);

                OverlayBorder.Opacity = 1.0;
                StateDot.Opacity = 1.0;

                OverlayBorder.BorderBrush = GreenBrush;
                OverlayTabContainer.BorderBrush = GreenBrush;
                StateDot.Fill = GreenBrush;

                TxtOverlayStatus.Text = !string.IsNullOrWhiteSpace(message) ? message : "Medição Concluída";
            });
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
        /// Reseta o ciclo de vida do overlay para 'Aguardando' limpo (ex: ao parar o serviço).
        /// </summary>
        public void ResetLifecycleState()
        {
            lock (_stateLock)
            {
                _dwellCts?.Cancel();
                _dwellCts?.Dispose();
                _dwellCts = null;
                _plcResetReceived = false;
                _dwellTimeElapsed = false;
                SetWaitingState();
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

                // A tira cobre a espessura da borda mais 10 pixels de tolerância interna para facilitar o clique
                double stripThickness = Math.Max(14, _borderThickness + 10);
                BorderResizeTop.Height = stripThickness;
                BorderResizeBottom.Height = stripThickness;
                BorderResizeLeft.Width = stripThickness;
                BorderResizeRight.Width = stripThickness;

                BorderResizeTop.Margin = new Thickness(0);
                BorderResizeBottom.Margin = new Thickness(0);
                BorderResizeLeft.Margin = new Thickness(0);
                BorderResizeRight.Margin = new Thickness(0);
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

                // Escala os elementos visuais proporcionalmente para chão de fábrica a longa distância
                double dotSize = Math.Round(_fontSize * 0.65);
                StateDot.Width = dotSize;
                StateDot.Height = dotSize;

                double iconSize = Math.Max(12, Math.Round(_fontSize * 0.85));
                IconSettingsViewbox.Width = iconSize;
                IconSettingsViewbox.Height = iconSize;
                IconRestoreViewbox.Width = iconSize;
                IconRestoreViewbox.Height = iconSize;

                double gripW = Math.Max(9, Math.Round(_fontSize * 0.65));
                double gripH = Math.Max(13, Math.Round(_fontSize * 0.95));
                GripViewbox.Width = gripW;
                GripViewbox.Height = gripH;

                double resizeGripSize = Math.Max(11, Math.Round(_fontSize * 0.75));
                if (ResizeGripViewbox != null)
                {
                    ResizeGripViewbox.Width = resizeGripSize;
                    ResizeGripViewbox.Height = resizeGripSize;
                }

                TxtBtnRestore.FontSize = Math.Max(10, _fontSize - 2);

                // Padding da aba cresce harmoniosamente com a fonte
                double padH = Math.Round(_fontSize * 0.9);
                double padV = Math.Round(_fontSize * 0.45);
                OverlayTabContainer.Padding = new Thickness(padH, padV, padH, padV);

                // Separadores
                if (TabContentPanel.Orientation == Orientation.Horizontal)
                {
                    double sepHeight = Math.Round(_fontSize * 1.1);
                    TabSeparator1.Height = sepHeight;
                    TabSeparator2.Height = sepHeight;
                    if (TabSeparator3 != null) TabSeparator3.Height = sepHeight;
                }
                else
                {
                    double sepWidth = Math.Round(_fontSize * 1.3);
                    TabSeparator1.Width = sepWidth;
                    TabSeparator2.Width = sepWidth;
                    if (TabSeparator3 != null) TabSeparator3.Width = sepWidth;
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
                        if (TabSeparator3 != null)
                        {
                            TabSeparator3.Width = 1; TabSeparator3.Height = sepHeight;
                            TabSeparator3.Margin = new Thickness(6, 0, 6, 0);
                        }

                        StateDot.Margin = new Thickness(0, 0, 8, 0);
                        GripHandle.Margin = new Thickness(0, 0, 8, 0);
                        if (TabResizeGrip != null) TabResizeGrip.Margin = new Thickness(4, 0, 0, 0);
                        if (TabEdgeResizeStrip != null)
                        {
                            TabEdgeResizeStrip.VerticalAlignment = VerticalAlignment.Top;
                            TabEdgeResizeStrip.HorizontalAlignment = HorizontalAlignment.Stretch;
                            TabEdgeResizeStrip.Height = 10;
                            TabEdgeResizeStrip.Width = double.NaN;
                            TabEdgeResizeStrip.Cursor = Cursors.SizeNS;
                            TabEdgeResizeStrip.Margin = new Thickness(-12, -6, -10, 0);
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
                        if (TabSeparator3 != null)
                        {
                            TabSeparator3.Width = sepWidth; TabSeparator3.Height = 1;
                            TabSeparator3.Margin = new Thickness(0, 6, 0, 6);
                        }

                        StateDot.Margin = new Thickness(0, 0, 0, 6);
                        GripHandle.Margin = new Thickness(0, 0, 0, 8);
                        if (TabResizeGrip != null) TabResizeGrip.Margin = new Thickness(0, 4, 0, 0);
                        if (TabEdgeResizeStrip != null)
                        {
                            TabEdgeResizeStrip.VerticalAlignment = VerticalAlignment.Stretch;
                            TabEdgeResizeStrip.HorizontalAlignment = HorizontalAlignment.Right;
                            TabEdgeResizeStrip.Width = 10;
                            TabEdgeResizeStrip.Height = double.NaN;
                            TabEdgeResizeStrip.Cursor = Cursors.SizeWE;
                            TabEdgeResizeStrip.Margin = new Thickness(0, -6, -10, -6);
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
                        if (TabSeparator3 != null)
                        {
                            TabSeparator3.Width = sepWidth; TabSeparator3.Height = 1;
                            TabSeparator3.Margin = new Thickness(0, 6, 0, 6);
                        }

                        StateDot.Margin = new Thickness(0, 0, 0, 6);
                        GripHandle.Margin = new Thickness(0, 0, 0, 8);
                        if (TabResizeGrip != null) TabResizeGrip.Margin = new Thickness(0, 4, 0, 0);
                        if (TabEdgeResizeStrip != null)
                        {
                            TabEdgeResizeStrip.VerticalAlignment = VerticalAlignment.Stretch;
                            TabEdgeResizeStrip.HorizontalAlignment = HorizontalAlignment.Left;
                            TabEdgeResizeStrip.Width = 10;
                            TabEdgeResizeStrip.Height = double.NaN;
                            TabEdgeResizeStrip.Cursor = Cursors.SizeWE;
                            TabEdgeResizeStrip.Margin = new Thickness(-12, -6, 0, -6);
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
                        if (TabSeparator3 != null)
                        {
                            TabSeparator3.Width = 1; TabSeparator3.Height = sepHeight;
                            TabSeparator3.Margin = new Thickness(6, 0, 6, 0);
                        }

                        StateDot.Margin = new Thickness(0, 0, 8, 0);
                        GripHandle.Margin = new Thickness(0, 0, 8, 0);
                        if (TabResizeGrip != null) TabResizeGrip.Margin = new Thickness(4, 0, 0, 0);
                        if (TabEdgeResizeStrip != null)
                        {
                            TabEdgeResizeStrip.VerticalAlignment = VerticalAlignment.Bottom;
                            TabEdgeResizeStrip.HorizontalAlignment = HorizontalAlignment.Stretch;
                            TabEdgeResizeStrip.Height = 10;
                            TabEdgeResizeStrip.Width = double.NaN;
                            TabEdgeResizeStrip.Cursor = Cursors.SizeNS;
                            TabEdgeResizeStrip.Margin = new Thickness(-12, 0, -10, -6);
                        }
                        TxtBtnRestore.Visibility = Visibility.Visible;
                        break;
                }

                UpdateTabMargin();
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
