using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConnectML.UI
{
    public class OverlaySettingsChangedEventArgs : EventArgs
    {
        public int BorderThickness { get; set; }
        public double FontSize { get; set; }
        public int HoldSeconds { get; set; }
        public string SnapPosition { get; set; } = "TOP";
        public bool EnableOverlay { get; set; } = true;
    }

    /// <summary>
    /// Diálogo flutuante para personalização do Widget Overlay HUD.
    /// Aberto a partir da engrenagem presente na aba flutuante.
    /// </summary>
    public partial class OverlaySettingsWindow : Window
    {
        public event EventHandler<OverlaySettingsChangedEventArgs>? SettingsChanged;
        public event EventHandler<OverlaySettingsChangedEventArgs>? SettingsSaved;

        private readonly OverlayWidgetWindow _overlayWindow;
        private bool _isInitializing = true;
        private string _currentSnap = "TOP";

        public OverlaySettingsWindow(OverlayWidgetWindow overlayWindow, 
                                     int borderThickness, 
                                     double fontSize, 
                                     int holdSeconds, 
                                     string snapPosition, 
                                     bool enableOverlay)
        {
            InitializeComponent();
            _overlayWindow = overlayWindow;
            _currentSnap = snapPosition.ToUpperInvariant();

            // Atribuição dos valores iniciais
            ChkEnableOverlay.IsChecked = enableOverlay;
            SliderBorderThickness.Value = Math.Clamp(borderThickness, 1, 35);
            SliderFontSize.Value = Math.Clamp(fontSize, 11, 40);
            SliderHoldSeconds.Value = Math.Clamp(holdSeconds, 1, 30);

            UpdateLabels();
            HighlightSnapButton(_currentSnap);

            _isInitializing = false;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            ApplyAndPersist();
            Close();
        }

        private void BtnSaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            ApplyAndPersist();
            Close();
        }

        private void ChkEnableOverlay_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            NotifyLiveChange();
        }

        private void SliderBorderThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            UpdateLabels();
            _overlayWindow.SetBorderThickness((int)SliderBorderThickness.Value);
            NotifyLiveChange();
        }

        private void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            UpdateLabels();
            _overlayWindow.SetFontSize(SliderFontSize.Value);
            NotifyLiveChange();
        }

        private void SliderHoldSeconds_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            UpdateLabels();
            NotifyLiveChange();
        }

        private void BtnSnap_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string pos)
            {
                _currentSnap = pos.ToUpperInvariant();
                HighlightSnapButton(_currentSnap);
                _overlayWindow.SetSnapPosition(_currentSnap);
                NotifyLiveChange();
            }
        }

        private void UpdateLabels()
        {
            if (TxtBorderThicknessValue != null)
                TxtBorderThicknessValue.Text = $"{(int)SliderBorderThickness.Value} px";

            if (TxtFontSizeValue != null)
            {
                int val = (int)SliderFontSize.Value;
                string label = val switch
                {
                    <= 12 => $"{val} pt (Compacto)",
                    <= 16 => $"{val} pt (Padrão)",
                    <= 22 => $"{val} pt (Grande)",
                    <= 30 => $"{val} pt (Extra Grande)",
                    _ => $"{val} pt (Ultra - Longa Distância)"
                };
                TxtFontSizeValue.Text = label;
            }

            if (TxtHoldSecondsValue != null)
                TxtHoldSecondsValue.Text = $"{(int)SliderHoldSeconds.Value} seg";
        }

        private void HighlightSnapButton(string activePos)
        {
            SetButtonActive(BtnSnapTop, activePos == "TOP");
            SetButtonActive(BtnSnapBottom, activePos == "BOTTOM");
            SetButtonActive(BtnSnapLeft, activePos == "LEFT");
            SetButtonActive(BtnSnapRight, activePos == "RIGHT");
        }

        private void SetButtonActive(Button? btn, bool isActive)
        {
            if (btn == null) return;
            if (isActive)
            {
                btn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0EA5E9"));
                btn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                btn.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#38BDF8"));
            }
            else
            {
                btn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B"));
                btn.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#94A3B8"));
                btn.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155"));
            }
        }

        private void NotifyLiveChange()
        {
            SettingsChanged?.Invoke(this, GetCurrentSettings());
        }

        private void ApplyAndPersist()
        {
            SettingsSaved?.Invoke(this, GetCurrentSettings());
        }

        public OverlaySettingsChangedEventArgs GetCurrentSettings()
        {
            return new OverlaySettingsChangedEventArgs
            {
                BorderThickness = (int)SliderBorderThickness.Value,
                FontSize = SliderFontSize.Value,
                HoldSeconds = (int)SliderHoldSeconds.Value,
                SnapPosition = _currentSnap,
                EnableOverlay = ChkEnableOverlay.IsChecked == true
            };
        }
    }
}
