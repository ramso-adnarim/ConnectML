using System;
using System.Runtime.InteropServices;
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

        private string _currentSnapPosition = "Top";
        private Storyboard? _pulseStoryboard;

        // Controle de Arraste (Drag & Drop)
        private bool _isDragging = false;
        private Point _dragStartMousePos;
        private double _dragStartTranslateX;
        private double _dragStartTranslateY;

        // Cores da Identidade Visual
        private static readonly Brush YellowBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
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
            // Garante que o Overlay preencha toda a área de trabalho do monitor
            Left = SystemParameters.WorkArea.Left;
            Top = SystemParameters.WorkArea.Top;
            Width = SystemParameters.WorkArea.Width;
            Height = SystemParameters.WorkArea.Height;

            _pulseStoryboard = (Storyboard)FindResource("PulseStoryboard");

            // Inicia em modo de espera (Aguardando Medição)
            SetWaitingState();
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

        #endregion

        #region Configurações de Borda e Snap

        /// <summary>
        /// Atualiza dinamicamente a espessura da borda perimetral luminosa.
        /// </summary>
        public void SetBorderThickness(int thickness)
        {
            thickness = Math.Clamp(thickness, 1, 12);
            OverlayBorder.BorderThickness = new Thickness(thickness);
        }

        /// <summary>
        /// Atualiza o posicionamento, layout e formato da aba de ancoragem.
        /// </summary>
        public void SetSnapPosition(string position)
        {
            _currentSnapPosition = position.ToUpperInvariant();

            Dispatcher.Invoke(() =>
            {
                switch (_currentSnapPosition)
                {
                    case "BOTTOM":
                        OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Center;
                        OverlayTabContainer.VerticalAlignment = VerticalAlignment.Bottom;
                        OverlayTabContainer.CornerRadius = new CornerRadius(8, 8, 0, 0);
                        OverlayTabContainer.BorderThickness = new Thickness(1, 1, 1, 0);
                        TabContentPanel.Orientation = Orientation.Horizontal;
                        TabSeparator.Width = 1;
                        TabSeparator.Height = 14;
                        TabSeparator.Margin = new Thickness(10, 0, 8, 0);
                        StateDot.Margin = new Thickness(0, 0, 8, 0);
                        TxtOverlayStatus.TextAlignment = TextAlignment.Left;
                        break;

                    case "LEFT":
                        OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Left;
                        OverlayTabContainer.VerticalAlignment = VerticalAlignment.Center;
                        OverlayTabContainer.CornerRadius = new CornerRadius(0, 8, 8, 0);
                        OverlayTabContainer.BorderThickness = new Thickness(0, 1, 1, 1);
                        TabContentPanel.Orientation = Orientation.Vertical;
                        TabSeparator.Width = 24;
                        TabSeparator.Height = 1;
                        TabSeparator.Margin = new Thickness(0, 8, 0, 8);
                        StateDot.Margin = new Thickness(0, 0, 0, 6);
                        TxtOverlayStatus.TextAlignment = TextAlignment.Center;
                        break;

                    case "RIGHT":
                        OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Right;
                        OverlayTabContainer.VerticalAlignment = VerticalAlignment.Center;
                        OverlayTabContainer.CornerRadius = new CornerRadius(8, 0, 0, 8);
                        OverlayTabContainer.BorderThickness = new Thickness(1, 1, 0, 1);
                        TabContentPanel.Orientation = Orientation.Vertical;
                        TabSeparator.Width = 24;
                        TabSeparator.Height = 1;
                        TabSeparator.Margin = new Thickness(0, 8, 0, 8);
                        StateDot.Margin = new Thickness(0, 0, 0, 6);
                        TxtOverlayStatus.TextAlignment = TextAlignment.Center;
                        break;

                    case "TOP":
                    default:
                        _currentSnapPosition = "TOP";
                        OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Center;
                        OverlayTabContainer.VerticalAlignment = VerticalAlignment.Top;
                        OverlayTabContainer.CornerRadius = new CornerRadius(0, 0, 8, 8);
                        OverlayTabContainer.BorderThickness = new Thickness(1, 0, 1, 1);
                        TabContentPanel.Orientation = Orientation.Horizontal;
                        TabSeparator.Width = 1;
                        TabSeparator.Height = 14;
                        TabSeparator.Margin = new Thickness(10, 0, 8, 0);
                        StateDot.Margin = new Thickness(0, 0, 8, 0);
                        TxtOverlayStatus.TextAlignment = TextAlignment.Left;
                        break;
                }

                SnapPositionChanged?.Invoke(this, _currentSnapPosition);
            });
        }

        #endregion

        #region Drag & Drop com Snap Magnético

        private void BtnRestoreMainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Impede que o clique no botão inicie o arrasto da aba
            e.Handled = false;
        }

        private void OverlayTabContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Se o clique foi no botão de restaurar, não inicia o arraste
            if (e.OriginalSource is DependencyObject dep && FindParent<Button>(dep) != null)
                return;

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

        #region Botão Restaurar Janela

        private void BtnRestoreMainWindow_Click(object sender, RoutedEventArgs e)
        {
            RequestRestoreMainWindow?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
