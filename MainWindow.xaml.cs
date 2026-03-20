using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Shapes; 
using Windows.Media.Control;
using System.Windows.Media.Effects;
using System.Net.NetworkInformation;
using Forms = System.Windows.Forms;

namespace DynamicIsland
{
    public partial class MainWindow : Window
    {
        private Forms.NotifyIcon _notifyIcon;
        private PerformanceCounter _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        private PerformanceCounter _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
        private double _totalRam = 16384; 
        private string _savePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "island_config.txt");
        
        private const double HiddenTop = -40; 
        private const double VisibleTop = 0;

        private System.Windows.Point _dragStartPoint; 
        private bool _isSticky = false, _isExpanded = false;
        private bool _isManualColor = false; 
        private string? _droppedFilePath = null;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private DispatcherTimer _leaveTimer;
        private Random _rnd = new Random();
        private double _lastSliderValue = 50;
        private double _baseBlurRadius = 65; 
        private Ping _pingSender = new Ping();
        private double _currentTemp = 0;
        private bool _isOverheating = false;

        [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        public MainWindow()
        {
            InitializeComponent();
    
            using (var p = System.Diagnostics.Process.GetCurrentProcess())
            {
                p.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
            }

            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline), 
                new FrameworkPropertyMetadata(60)
            );
            
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = HiddenTop; 
            this.ShowInTaskbar = false;
            
            InitTrayIcon();

            try {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")) {
                    foreach (var obj in searcher.Get()) _totalRam = Convert.ToDouble(obj["TotalPhysicalMemory"]) / 1024 / 1024;
                }
            } catch { _totalRam = 16384; }

            _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _leaveTimer.Tick += (s, e) => { 
                if (!IsMouseOver && !_isSticky && !_isExpanded) MoveIsland(HiddenTop); 
            };

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; 
            timer.Tick += (s, e) => { Timer_Tick(); UpdateStats(); };
            timer.Start();

            var tempTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            tempTimer.Tick += (s, e) => { UpdateTemperature(); };
            tempTimer.Start();
            LoadSettings(); 
        }
        
        private void InitTrayIcon()
        {
            _notifyIcon = new Forms.NotifyIcon();

            try 
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
                _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            catch 
            {
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Forms.Application.ExecutablePath);
            }

            _notifyIcon.Visible = true;
            _notifyIcon.Text = "Dynamic Island 靈動島";

            _notifyIcon.MouseClick += (s, e) => {
                if (e.Button == Forms.MouseButtons.Left) {
                    this.Show();            
                    this.WindowState = WindowState.Normal; 
                    MoveIsland(HiddenTop); 
                    this.Activate();        
                }
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("顯示靈動島", null, (s, e) => MoveIsland(VisibleTop));
            contextMenu.Items.Add("離開程式", null, (s, e) => Exit_Click(null!, null!));
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_notifyIcon != null) _notifyIcon.Dispose();
            base.OnClosed(e);
        }

        private void LoadSettings() {
            try {
                if (System.IO.File.Exists(_savePath)) {
                    string[] data = System.IO.File.ReadAllText(_savePath).Split('|');
                    if (data.Length >= 1) UpdateSystemColor((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(data[0]), true);
                    if (data.Length >= 2) { _baseBlurRadius = double.Parse(data[1]); IslandGlow.BlurRadius = _baseBlurRadius; }
                }
            } catch { }
        }
        
        private void UpdateTemperature()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    double temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    _currentTemp = (temp - 2732) / 10.0;
                }

                Dispatcher.Invoke(() => {
                    string[] parts = CpuText.Text.Split('|');
                    CpuText.Text = $"{parts[0].Trim()} | {(int)_currentTemp}°C";

                    if (_currentTemp >= 80)
                    {
                        if (!_isOverheating) StartOverheatAlert();
                    }
                    else if (_isOverheating)
                    {
                        StopOverheatAlert();
                    }
                });
            }
            catch 
            { 
                Dispatcher.Invoke(() => {
                    if (!CpuText.Text.Contains("°C")) CpuText.Text += " | --°C";
                });
            }
        }

        private void StartOverheatAlert()
        {
            _isOverheating = true;
            IslandGlow.Color = System.Windows.Media.Colors.Red;

            DoubleAnimation overheatBreathe = new DoubleAnimation
            {
                From = 40,
                To = 110,
                Duration = TimeSpan.FromMilliseconds(400),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            IslandGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, overheatBreathe);
        }

        private void StopOverheatAlert()
        {
            _isOverheating = false;
            IslandGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            IslandGlow.BlurRadius = _baseBlurRadius;
            if (!_isManualColor) IslandGlow.Color = System.Windows.Media.Colors.LimeGreen;
        }
        
        private async void UpdatePing()
        {
            try {
                PingReply reply = await _pingSender.SendPingAsync("8.8.8.8", 1000);
                if (reply.Status == IPStatus.Success) {
                    long p = reply.RoundtripTime;
                    PingText.Text = $"P: {p}ms";
            
                    if (!_isManualColor) {
                        if (p > 220) IslandGlow.Color = System.Windows.Media.Colors.Red;
                        else if (p > 60) IslandGlow.Color = System.Windows.Media.Colors.Orange;
                        else IslandGlow.Color = System.Windows.Media.Colors.LimeGreen;
                    }
                }
            } catch { PingText.Text = "P: Error"; }
        }

        private void UpdateStats()
        {
            float cpu = _cpuCounter.NextValue();
            float ramAvail = _ramCounter.NextValue();
            double ramUsed = 100 - (ramAvail / _totalRam * 100);
            CpuText.Text = $"C: {(int)cpu}%";
            RamText.Text = $"R: {(int)ramUsed}%";

            bool isPlaying = false;
            if (_currentSession != null) {
                isPlaying = _currentSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }

            System.Windows.Shapes.Rectangle[] bars = { Bar1, Bar2, Bar3, Bar4 };
            foreach (var bar in bars) {
                double h = isPlaying ? _rnd.Next(8, 25) : (cpu / 5) + 3;
                bar.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(150)));
            }

            if (isPlaying) {
                double jumpBlur = _baseBlurRadius * (0.8 + _rnd.NextDouble() * 0.8); 
                IslandGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, 
                    new DoubleAnimation(jumpBlur, TimeSpan.FromMilliseconds(100)));
            } else {
                IslandGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, 
                    new DoubleAnimation(_baseBlurRadius, TimeSpan.FromMilliseconds(400)));
            }
        }

        public void Collapse() { 
            _isExpanded = false; 
            ControlPanel.BeginAnimation(HeightProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))); 
            IslandBorder.BeginAnimation(WidthProperty, new DoubleAnimation(330, TimeSpan.FromMilliseconds(300))); 
            ControlPanel.Opacity = 0;
            if (!IsMouseOver) MoveIsland(HiddenTop); 
        }

        private void Expand() { 
            _isExpanded = true; 
            ControlPanel.BeginAnimation(HeightProperty, new DoubleAnimation(350, TimeSpan.FromMilliseconds(400))); 
            IslandBorder.BeginAnimation(WidthProperty, new DoubleAnimation(500, TimeSpan.FromMilliseconds(400))); 
            ControlPanel.Opacity = 1; 
            MoveIsland(VisibleTop);
        }

        private void Handle_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { 
            _leaveTimer.Stop(); 
            if (!_isSticky) MoveIsland(VisibleTop); 
            StartMarquee(); 
        }

        private void Handle_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { 
            if (!_isSticky && !_isExpanded) _leaveTimer.Start(); 
        }

        private void Window_Deactivated(object sender, EventArgs e) { if (_isExpanded) Collapse(); }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (IsLoaded) {
                double delta = e.NewValue - _lastSliderValue;
                if (Math.Abs(delta) >= 2.5) {
                    for (int i = 0; i < (int)(Math.Abs(delta) / 2.5); i++) 
                        keybd_event(delta > 0 ? (byte)0xAF : (byte)0xAE, 0, 0, 0);
                    _lastSliderValue = e.NewValue;
                }
            }
        }

        public void UpdateSystemColor(System.Windows.Media.Color c, bool isManual = false) {
            if (!isManual && _isManualColor) return; 
            if (isManual) { _isManualColor = true; _baseBlurRadius = IslandGlow.BlurRadius; SaveSettings(c); }
            IslandGlow.Color = c;
            var brush = new SolidColorBrush(c);
            MusicProgress.Foreground = brush;
            Bar1.Fill = Bar2.Fill = Bar3.Fill = Bar4.Fill = brush;
            IslandBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, (byte)(c.R * 0.2), (byte)(c.G * 0.2), (byte)(c.B * 0.2)));
            ControlPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, (byte)(c.R * 0.1), (byte)(c.G * 0.1), (byte)(c.B * 0.1)));
        }
        
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_notifyIcon == null)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true; 
            this.Hide(); 

            try {
                _notifyIcon.Visible = true;
            } catch {
                e.Cancel = false; 
            }
        }
        private async void Timer_Tick() {
            try {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _currentSession = manager.GetCurrentSession();
                if (_currentSession != null) {
                    var info = await _currentSession.TryGetMediaPropertiesAsync();
                    bool isPlaying = _currentSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    PlayPauseBtn.Content = isPlaying ? "⏸" : "▶";
                    StatusText.Text = info.Title ?? "正在播放";
                    if (info.Thumbnail != null) {
                        using var stream = await info.Thumbnail.OpenReadAsync();
                        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = stream.AsStream(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
                        AlbumArtImage.Source = bitmap; AlbumArtBorder.Visibility = Visibility.Visible;
                        UpdateSystemColor(GetMajorColor(bitmap));
                    }
                } else { StatusText.Text = DateTime.Now.ToString("HH:mm"); AlbumArtBorder.Visibility = Visibility.Collapsed; PlayPauseBtn.Content = "▶"; }
            } catch { }
        }

        private void MoveIsland(double t) => this.BeginAnimation(TopProperty, new DoubleAnimation(t, TimeSpan.FromMilliseconds(300)));
        private void Island_Click(object s, System.Windows.Input.MouseButtonEventArgs e) { if (!_isExpanded) Expand(); else Collapse(); }
        private void StartMarquee() { if (StatusText.ActualWidth <= TextCanvas.Width) return; StatusText.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(0, -StatusText.ActualWidth + 100, TimeSpan.FromSeconds(8)) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true }); }
        private void StopMarquee() { StatusText.BeginAnimation(Canvas.LeftProperty, null); Canvas.SetLeft(StatusText, 0); }
        private void Exit_Click(object s, RoutedEventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null; 
            }

            System.Windows.Application.Current.Shutdown();
        }
        private void OpenColorPicker_Click(object s, RoutedEventArgs e) => new ColorSettings(this).Show();
        private void ToggleSticky_Click(object s, RoutedEventArgs e) { _isSticky = MenuSticky.IsChecked; MoveIsland(VisibleTop); }
        private void Island_Drop(object s, System.Windows.DragEventArgs e) { string[] f = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop); if (f != null && f.Length > 0) { _droppedFilePath = f[0]; DropZoneText.Text = "📄 已暫存: " + System.IO.Path.GetFileName(_droppedFilePath); DropZoneText.Foreground = System.Windows.Media.Brushes.LimeGreen; } }
        private void Island_DragOver(object s, System.Windows.DragEventArgs e) => e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        private void DropZone_PreviewMouseLeftButtonDown(object s, System.Windows.Input.MouseButtonEventArgs e) => _dragStartPoint = e.GetPosition(null);
        private void DropZone_MouseMove(object s, System.Windows.Input.MouseEventArgs e) { Vector diff = _dragStartPoint - e.GetPosition(null); if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _droppedFilePath != null && (Math.Abs(diff.X) > 5 || Math.Abs(diff.Y) > 5)) { if (DragDrop.DoDragDrop(this, new System.Windows.Forms.DataObject(System.Windows.Forms.DataFormats.FileDrop, new string[] { _droppedFilePath }), System.Windows.DragDropEffects.Copy) != System.Windows.DragDropEffects.None) { _droppedFilePath = null; DropZoneText.Text = "📂 拖放檔案至此處中轉"; DropZoneText.Foreground =  System.Windows.Media.Brushes.Gray; } } }
        private void DropZone_MouseDown(object s, System.Windows.Input.MouseButtonEventArgs e) { if (e.ClickCount == 2 && _droppedFilePath != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_droppedFilePath}\""); }
        private void SetBrightness(int t) { try { using var mc = new ManagementClass("WmiMonitorBrightnessMethods") { Scope = new ManagementScope(@"\\.\root\wmi") }; foreach (ManagementObject i in mc.GetInstances()) i.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, (byte)t }); } catch { } }
        private void BrightnessUp_Click(object s, RoutedEventArgs e) => SetBrightness(80);
        private void BrightnessDown_Click(object s, RoutedEventArgs e) => SetBrightness(20);
        private void Mute_Click(object s, RoutedEventArgs e) { keybd_event(0xAD, 0, 0, 0); }
        private async void PlayPause_Click(object s, RoutedEventArgs e) { if (_currentSession != null) await _currentSession.TryTogglePlayPauseAsync(); }
        private async void Prev_Click(object s, RoutedEventArgs e) { if (_currentSession != null) await _currentSession.TrySkipPreviousAsync(); }
        private async void Next_Click(object s, RoutedEventArgs e) { if (_currentSession != null) await _currentSession.TrySkipNextAsync(); }
        public void SaveSettings(System.Windows.Media.Color c) { try { System.IO.File.WriteAllText(_savePath, $"{c.ToString()}|{_baseBlurRadius}"); } catch { } }
        private System.Windows.Media.Color GetMajorColor(BitmapSource b) { byte[] p = new byte[400]; b.CopyPixels(new Int32Rect((int)b.PixelWidth/2-5, (int)b.PixelHeight/2-5, 10, 10), p, 40, 0); long r=0, g=0, bl=0; for(int i=0; i<p.Length; i+=4){ bl+=p[i]; g+=p[i+1]; r+=p[i+2]; } return System.Windows.Media.Color.FromRgb((byte)(r/100), (byte)(g/100), (byte)(bl/100)); }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);
        }
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) { return IntPtr.Zero; }
    }
}