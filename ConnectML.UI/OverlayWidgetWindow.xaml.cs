using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ConnectML.UI
{
    /// <summary>
    /// Janela de Overlay HUD do ConnectML.
    /// Exibida em tela cheia de forma transparente sem interceptar cliques do usuário no centro da tela.
    /// </summary>
    public partial class OverlayWidgetWindow : Window
    {
        // Evento disparado quando o usuário clica no botão para restaurar a janela principal
        public event EventHandler? RequestRestoreMainWindow;

        // Evento disparado quando a posição de acoplamento da aba é alterada
        public event EventHandler<string>? SnapPositionChanged;

        private string _currentSnapPosition = "Top";

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
        }

        /// <summary>
        /// Atualiza dinamicamente a espessura da borda perimetral luminosa.
        /// </summary>
        public void SetBorderThickness(int thickness)
        {
            thickness = Math.Clamp(thickness, 1, 12);
            OverlayBorder.BorderThickness = new Thickness(thickness);
        }

        /// <summary>
        /// Atualiza o posicionamento e formato da aba de ancoragem.
        /// </summary>
        public void SetSnapPosition(string position)
        {
            _currentSnapPosition = position;
            SnapPositionChanged?.Invoke(this, position);

            switch (position.ToUpperInvariant())
            {
                case "BOTTOM":
                    OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Center;
                    OverlayTabContainer.VerticalAlignment = VerticalAlignment.Bottom;
                    OverlayTabContainer.CornerRadius = new CornerRadius(8, 8, 0, 0);
                    OverlayTabContainer.BorderThickness = new Thickness(1, 1, 1, 0);
                    break;

                case "LEFT":
                    OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Left;
                    OverlayTabContainer.VerticalAlignment = VerticalAlignment.Center;
                    OverlayTabContainer.CornerRadius = new CornerRadius(0, 8, 8, 0);
                    OverlayTabContainer.BorderThickness = new Thickness(0, 1, 1, 1);
                    break;

                case "RIGHT":
                    OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Right;
                    OverlayTabContainer.VerticalAlignment = VerticalAlignment.Center;
                    OverlayTabContainer.CornerRadius = new CornerRadius(8, 0, 0, 8);
                    OverlayTabContainer.BorderThickness = new Thickness(1, 1, 0, 1);
                    break;

                case "TOP":
                default:
                    OverlayTabContainer.HorizontalAlignment = HorizontalAlignment.Center;
                    OverlayTabContainer.VerticalAlignment = VerticalAlignment.Top;
                    OverlayTabContainer.CornerRadius = new CornerRadius(0, 0, 8, 8);
                    OverlayTabContainer.BorderThickness = new Thickness(1, 0, 1, 1);
                    break;
            }
        }

        private void BtnRestoreMainWindow_Click(object sender, RoutedEventArgs e)
        {
            RequestRestoreMainWindow?.Invoke(this, EventArgs.Empty);
        }
    }
}
