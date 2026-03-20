using System.Windows;
using System.Windows.Media;

namespace DynamicIsland
{
    public partial class ColorSettings : Window
    {
        private MainWindow _main;

        public ColorSettings(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            if (_main != null)
            {
                ColorPicker.SelectedColor = _main.IslandGlow.Color;
                GlowSlider.Value = _main.IslandGlow.BlurRadius;
            }
        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e)
        {
            if (e.NewValue.HasValue && _main != null)
            {
                _main.UpdateSystemColor(e.NewValue.Value, true); 
            }
        }

        private void GlowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_main != null && _main.IslandGlow != null)
            {
                _main.IslandGlow.BlurRadius = e.NewValue;
                _main.SaveSettings(_main.IslandGlow.Color);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}