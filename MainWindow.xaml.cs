using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using HidSharp;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CyberGrip
{
    public partial class MainWindow : Window
    {
        private readonly ViaCommunicator _viaCommunicator;
        private readonly DispatcherTimer _deviceChangeTimer;
        private bool _isConnected = false;
        private bool _isConnecting;
        private bool _vialExternalToolLaunched;
        private bool _loadingJoystickSettings;
        private bool _physicalKeyCaptureRunning;
        private double? _usbPollingRateHz;
        private double? _windowHeightBeforeConsole;
        private double? _windowTopBeforeConsole;
        private CancellationTokenSource? _physicalKeyCaptureCancellationToken;
        private Task? _physicalKeyCaptureTask;
        private readonly Forms.NotifyIcon _trayIcon;
        private readonly List<string> _recentProfiles = new();
        private Forms.ToolStripMenuItem? _loadProfileMenu;

        // Position action labels
        private readonly Dictionary<int, string> _positionLabels = new()
        {
            // Pinky Pod (Keys 00 - 04)
            { 0, "Pinky Extra" },
            { 1, "High Fwd" },
            { 2, "Push Fwd" },
            { 3, "Press Down" },
            { 4, "Pull Back" },

            // Ring Pod (Keys 05 - 08)
            { 5, "High Fwd" },
            { 6, "Push Fwd" },
            { 7, "Press Down" },
            { 8, "Pull Back" },

            // Middle Pod (Keys 09 - 12)
            { 9, "High Fwd" },
            { 10, "Push Fwd" },
            { 11, "Press Down" },
            { 12, "Pull Back" },

            // Index Pod (Keys 13 - 16)
            { 13, "High Fwd" },
            { 14, "Push Fwd" },
            { 15, "Press Down" },
            { 16, "Pull Back" },
            { 17, "Index Extra" },

            // Thumb Cluster (Key 18)
            { 18, "Thumb Click" }
        };

        public MainWindow()
        {
            InitializeComponent();
            string assetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
            string backgroundPath = Path.Combine(assetsDirectory, "cyber_bg.jpg");
            if (File.Exists(backgroundPath))
                BackgroundArtwork.Source = new BitmapImage(new Uri(backgroundPath, UriKind.Absolute));

            string iconPath = Path.Combine(assetsDirectory, "app.ico");
            if (File.Exists(iconPath))
                Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));

            _viaCommunicator = new ViaCommunicator();
            LoadRecentProfiles();
            _trayIcon = CreateTrayIcon();

            _deviceChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _deviceChangeTimer.Tick += DeviceChangeTimer_Tick;
            DeviceList.Local.Changed += DeviceList_Changed;
            Closed += MainWindow_Closed;
        }

        private void BtnToggleConsole_Click(object sender, RoutedEventArgs e)
        {
            if (ConsolePanel.Visibility == Visibility.Visible)
            {
                ConsolePanel.Visibility = Visibility.Collapsed;
                BtnToggleConsole.Content = "Console";
                if (_windowHeightBeforeConsole.HasValue && _windowTopBeforeConsole.HasValue)
                {
                    Height = _windowHeightBeforeConsole.Value;
                    Top = _windowTopBeforeConsole.Value;
                }
                _windowHeightBeforeConsole = null;
                _windowTopBeforeConsole = null;
            }
            else
            {
                _windowHeightBeforeConsole = Height;
                _windowTopBeforeConsole = Top;
                ConsolePanel.Visibility = Visibility.Visible;
                BtnToggleConsole.Content = "Hide Console";

                Rect workArea = SystemParameters.WorkArea;
                double expandedHeight = Math.Min(Height + ConsolePanel.Height, workArea.Height - 16);
                Height = expandedHeight;
                Top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - expandedHeight));
                Dispatcher.BeginInvoke(new Action(() => TxtLog.ScrollToEnd()), DispatcherPriority.Loaded);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FitWindowToWorkArea();
            // Indicate auto-connect attempt in UI
            BtnConnect_Click(sender, e);
        }

        private void FitWindowToWorkArea()
        {
            Rect workArea = SystemParameters.WorkArea;
            double availableWidth = Math.Max(240, workArea.Width - 16);
            double availableHeight = Math.Max(240, workArea.Height - 8);

            Width = Math.Min(Width, availableWidth);
            Height = Math.Min(Height, availableHeight);
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;
            UpdateTabScrolling();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTabScrolling();
        }

        private void UpdateTabScrolling()
        {
            if (TabsScrollViewer == null)
                return;

            TabsScrollViewer.HorizontalScrollBarVisibility = Width < 1080
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_vialExternalToolLaunched)
            {
                var result = MessageBox.Show(
                    "Close Vial Web or the Vial desktop app before reconnecting CyberGrip. Is Vial closed?",
                    "Reconnect to CyberGrip", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;

                _vialExternalToolLaunched = false;
            }

            await ConnectAndSyncAsync();
        }

        private async Task ConnectAndSyncAsync()
        {
            if (_isConnecting)
                return;

            _isConnecting = true;
            Stopwatch connectionTimer = Stopwatch.StartNew();
            BtnConnect.IsEnabled = false;
            SetConnectionIndicator("SEARCHING...", Color.FromRgb(0xFF, 0xC1, 0x07));
            TxtLog.Clear();
            LogDebug("Searching for CyberGrip 18 raw HID interface...");
            TxtStatus.Text = "Status: Searching for CyberGrip 18...";

            try
            {
                ushort version = await Task.Run(_viaCommunicator.GetProtocolVersion);

                if (version == 0)
                {
                    SetDisconnectedState("Device Not Found!", "Error: Could not locate CyberGrip 18 raw HID interface.\n");
                    return;
                }

                byte layerCount = await Task.Run(_viaCommunicator.GetLayerCount);
                double? usbPollingRate = await Task.Run(_viaCommunicator.GetUsbPollingRateHz);
                _usbPollingRateHz = usbPollingRate;
                byte selectedLayer = (byte)(CmbLayer.SelectedIndex >= 0 ? CmbLayer.SelectedIndex : 0);
                List<ushort> keycodes = await Task.Run(() => _viaCommunicator.ReadLayerKeycodes(selectedLayer, 19));

                _isConnected = true;
                SetConnectionIndicator("CONNECTED", Color.FromRgb(0x00, 0xE6, 0x76));
                TxtUsbPollingRate.Text = usbPollingRate.HasValue
                    ? $"{usbPollingRate.Value:N0} Hz"
                    : "--- Hz";
                UpdateMatrixTheoreticalMax();

                TxtStatus.Text = "Status: Connected!";
                LogDebug($"Connected; VIA protocol {version}; {layerCount} hardware layers.");
                LogDebug(usbPollingRate.HasValue
                    ? $"USB interrupt-IN polling rate: {usbPollingRate.Value:N0} Hz; request/response estimate: {usbPollingRate.Value / 2.0:N0} reads/s."
                    : "USB interrupt-IN polling rate could not be read from the device descriptor.");

                // Populate layer choices without triggering a series of device reads.
                CmbLayer.SelectionChanged -= CmbLayer_SelectionChanged;
                CmbLayer.Items.Clear();
                for (byte i = 0; i < layerCount; i++)
                    CmbLayer.Items.Add(new ComboBoxItem { Content = $"Layer {i}" });

                if (layerCount > 0)
                {
                    selectedLayer = (byte)Math.Min(selectedLayer, layerCount - 1);
                    CmbLayer.SelectedIndex = selectedLayer;
                }
                else
                {
                    selectedLayer = 0;
                }

                CmbLayer.IsEnabled = layerCount > 0;
                CmbLayer.SelectionChanged += CmbLayer_SelectionChanged;

                DisplayLayerInGui(selectedLayer, keycodes);
                await LoadJoystickInversionForLayerAsync(selectedLayer);
                LogDebug($"Initial layer {selectedLayer} loaded; connection setup took {connectionTimer.ElapsedMilliseconds} ms.");
                if (MainTabs.SelectedIndex == 1 && !_pollingTestRunning)
                    StartMatrixPolling();
            }
            catch (Exception ex)
            {
                LogDebug($"Connection setup exception: {ex}");
                SetDisconnectedState("Connection Error", $"Error while connecting: {ex.Message}\n");
            }
            finally
            {
                _isConnecting = false;
                BtnConnect.IsEnabled = true;
            }
        }

        private async void JoystickInvert_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingJoystickSettings || !_isConnected)
                return;

            bool invertX = ChkInvertX.IsChecked == true;
            bool invertY = ChkInvertY.IsChecked == true;
            byte layer = (byte)(CmbLayer.SelectedIndex >= 0 ? CmbLayer.SelectedIndex : 0);
            bool applied = await Task.Run(() =>
                _viaCommunicator.SetJoystickInversion(layer, invertX, invertY));
            LogDebug(applied
                ? $"Joystick inversion saved for Layer {layer}: X={invertX}, Y={invertY}."
                : $"Joystick inversion write failed for Layer {layer}: X={invertX}, Y={invertY}.");
            TxtJoystickSettingsStatus.Text = applied
                ? $"Saved for Layer {layer}"
                : "Firmware update required for per-layer axis settings";
            if (!applied)
            {
                ChkInvertX.IsEnabled = false;
                ChkInvertY.IsEnabled = false;
            }
        }

        private void DeviceList_Changed(object? sender, EventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                _deviceChangeTimer.Stop();
                _deviceChangeTimer.Start();
            });
        }

        private async void DeviceChangeTimer_Tick(object? sender, EventArgs e)
        {
            _deviceChangeTimer.Stop();
            if (_isConnecting)
            {
                _deviceChangeTimer.Start();
                return;
            }

            bool present;
            try
            {
                present = await Task.Run(_viaCommunicator.IsDevicePresent);
            }
            catch
            {
                return;
            }

            if (present && !_isConnected && !_vialExternalToolLaunched)
            {
                LogDebug("CyberGrip 18 detected; reconnecting.");
                await ConnectAndSyncAsync();
            }
            else if (!present && _isConnected)
                SetDisconnectedState("Device Disconnected", "CyberGrip 18 disconnected. Waiting for it to reconnect...\n");
        }

        private void SetDisconnectedState(string status, string logMessage)
        {
            bool wasConnected = _isConnected;
            _isConnected = false;
            _physicalKeyCaptureCancellationToken?.Cancel();
            ChkInvertX.IsEnabled = false;
            ChkInvertY.IsEnabled = false;

            if (_pollingTestRunning)
                StopPollingRateTest();
            StopMatrixPolling();

            TxtStatus.Text = $"Status: {status}";
            SetConnectionIndicator("DISCONNECTED", Color.FromRgb(0xFF, 0x52, 0x68));
            TxtUsbPollingRate.Text = "--- Hz";
            _usbPollingRateHz = null;
            UpdateMatrixTheoreticalMax();
            CmbLayer.IsEnabled = false;
            if (wasConnected || status != "Device Disconnected")
                LogDebug(logMessage.Trim());
        }

        private void SetConnectionIndicator(string text, Color color)
        {
            var brush = new SolidColorBrush(color);
            TxtConnectionState.Text = text;
            TxtConnectionState.Foreground = brush;
            ConnectionStatusDot.Fill = brush;
        }

        private void LogDebug(string message)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => LogDebug(message)));
                return;
            }

            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        }

        private void TxtLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ConsolePanel.Visibility == Visibility.Visible)
                TxtLog.ScrollToEnd();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            DeviceList.Local.Changed -= DeviceList_Changed;
            _deviceChangeTimer.Stop();
            _isConnected = false;
            _physicalKeyCaptureCancellationToken?.Cancel();
            if (_pollingTestRunning)
                StopPollingRateTest();
            StopMatrixPolling();
        }

        private async void CmbLayer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isConnected || CmbLayer.SelectedIndex < 0) return;

            byte selectedLayer = (byte)CmbLayer.SelectedIndex;
            LogDebug($"Selected Layer {selectedLayer} in the programming view.");
            LoadLayerToGUI(selectedLayer);
            await LoadJoystickInversionForLayerAsync(selectedLayer);
        }

        private void BtnLayerHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new Window
            {
                Title = "How to Change Layers",
                Owner = this,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x2B)),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                ShowInTaskbar = false
            };

            var content = new StackPanel { Margin = new Thickness(22), MaxWidth = 700 };
            content.Children.Add(new TextBlock
            {
                Text = "Hold the three gold keys, then press one blue key to choose a layer.",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var fingers = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            AddLayerFinger(fingers, "PINKY", "Pinky Extra", "HOLD", true,
                ("High Fwd", "LAYER 0"), ("Push Fwd", ""), ("Press Down", "HOLD"), ("Pull Back", ""));
            AddLayerFinger(fingers, "RING", null, null, false,
                ("High Fwd", "LAYER 1"), ("Push Fwd", ""), ("Press Down", "HOLD"), ("Pull Back", ""));
            AddLayerFinger(fingers, "MIDDLE", null, null, false,
                ("High Fwd", "LAYER 2"), ("Push Fwd", ""), ("Press Down", ""), ("Pull Back", ""));
            AddLayerFinger(fingers, "INDEX", "Index Extra", "", false,
                ("High Fwd", "LAYER 3"), ("Push Fwd", ""), ("Press Down", ""), ("Pull Back", ""));
            content.Children.Add(fingers);

            content.Children.Add(new TextBlock
            {
                Text = "The shortcut works from any layer. These keys still send their normal keystrokes to your game or app.",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 14)
            });

            var closeButton = new Button
            {
                Content = "Got it",
                Width = 90,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
                Style = (Style)FindResource("KeyCapStyle")
            };
            closeButton.Click += (_, _) => helpWindow.Close();
            content.Children.Add(closeButton);
            helpWindow.Content = content;
            helpWindow.ShowDialog();
        }

        private static void AddLayerFinger(Panel parent, string fingerName, string? extraKey, string? extraTag, bool extraOnLeft,
            params (string Key, string Tag)[] keys)
        {
            bool hasExtra = extraKey != null;
            var finger = new StackPanel { Width = hasExtra ? 190 : 112, Margin = new Thickness(4, 0, 4, 0) };
            finger.Children.Add(new TextBlock
            {
                Text = fingerName,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xB3, 0xC2)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 7)
            });

            var keyGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(hasExtra && extraOnLeft ? 78 : 104) });
            if (hasExtra)
                keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(extraOnLeft ? 104 : 78) });
            for (int i = 0; i < keys.Length; i++)
                keyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int row = 0; row < keys.Length; row++)
            {
                var mainKey = CreateLayerKeyCard(keys[row].Key, keys[row].Tag, 104);
                int mainColumn = hasExtra && extraOnLeft ? 1 : 0;
                Grid.SetRow(mainKey, row);
                Grid.SetColumn(mainKey, mainColumn);
                keyGrid.Children.Add(mainKey);

                if (row == 2 && hasExtra)
                {
                    var extra = CreateLayerKeyCard(extraKey!, extraTag ?? "", 76);
                    Grid.SetRow(extra, row);
                    Grid.SetColumn(extra, extraOnLeft ? 0 : 1);
                    keyGrid.Children.Add(extra);
                }
            }

            finger.Children.Add(keyGrid);
            parent.Children.Add(finger);
        }

        private static Border CreateLayerKeyCard(string key, string tag, double width)
        {
            bool isHold = tag == "HOLD";
            bool isLayer = tag.StartsWith("LAYER", StringComparison.Ordinal);
            var keyCard = new Border
                {
                    Background = new SolidColorBrush(isHold
                        ? Color.FromRgb(0x8A, 0x5A, 0x13)
                        : isLayer ? Color.FromRgb(0x00, 0x65, 0x80) : Color.FromRgb(0x2A, 0x34, 0x42)),
                    BorderBrush = new SolidColorBrush(isHold
                        ? Color.FromRgb(0xFF, 0xC1, 0x07)
                        : isLayer ? Color.FromRgb(0x00, 0xE5, 0xFF) : Color.FromRgb(0x45, 0x54, 0x67)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Width = width,
                    Height = 46,
                    Margin = new Thickness(2, 0, 2, 5),
                    Padding = new Thickness(3)
                };
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock
                {
                    Text = key,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                });
                if (!string.IsNullOrEmpty(tag))
                {
                    label.Children.Add(new TextBlock
                    {
                        Text = tag,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(isHold ? Color.FromRgb(0xFF, 0xD5, 0x62) : Color.FromRgb(0x67, 0xEE, 0xFF)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }
                keyCard.Child = label;
                return keyCard;
        }

        private async Task LoadJoystickInversionForLayerAsync(byte layer)
        {
            var inversionState = await Task.Run(() => _viaCommunicator.GetJoystickInversion(layer));
            _loadingJoystickSettings = true;
            ChkInvertX.IsChecked = inversionState.InvertX;
            ChkInvertY.IsChecked = inversionState.InvertY;
            _loadingJoystickSettings = false;
            ChkInvertX.IsEnabled = inversionState.Success;
            ChkInvertY.IsEnabled = inversionState.Success;
            TxtJoystickSettingsStatus.Text = inversionState.Success
                ? $"Saved for Layer {layer}"
                : "Firmware update required for per-layer axis settings";
            TxtJoystickSettingsStatus.Foreground = new SolidColorBrush(inversionState.Success
                ? Color.FromRgb(0x00, 0xE6, 0x76)
                : Color.FromRgb(0xFF, 0xC1, 0x07));
        }

        private void LoadLayerToGUI(byte layer)
        {
            byte totalKeys = 19;
            List<ushort> keycodes = _viaCommunicator.ReadLayerKeycodes(layer, totalKeys);

            DisplayLayerInGui(layer, keycodes);
        }

        private void DisplayLayerInGui(byte layer, List<ushort> keycodes)
        {
            TxtLog.AppendText($"--- Loading Layer {layer} to GUI ---\n");

            for (int i = 0; i < keycodes.Count; i++)
            {
                TxtLog.AppendText($"Key {i:D2}: 0x{keycodes[i]:X4}\n");
                UpdateKeyCapLabel(i, keycodes[i]);
            }

            TxtStatus.Text = $"Status: Active - Layer {layer} Displayed";
        }

        private void UpdateKeyCapLabel(int index, ushort keycode)
        {
            UpdateSingleButton(GetKeyButton(index, testing: false), index, keycode);
            UpdateSingleButton(GetKeyButton(index, testing: true), index, keycode);
        }

        private Button? GetKeyButton(int index, bool testing)
        {
            return FindName($"{(testing ? "BtnTestKey" : "BtnKey")}{index:D2}") as Button;
        }

        private void UpdateSingleButton(Button? targetButton, int index, ushort keycode)
        {
            if (targetButton != null)
            {
                string actionLabel = _positionLabels.TryGetValue(index, out var label) ? label : $"Key {index:D2}";
                string friendlyKey = GetFriendlyKeyName(keycode);

                bool isTransparent = keycode == 0x0001;

                var container = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var headerText = new TextBlock
                {
                    Text = actionLabel.ToUpper(),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(isTransparent
                        ? Color.FromRgb(0x55, 0x55, 0x66)
                        : Color.FromRgb(0x88, 0x88, 0x99)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                var separator = new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Width = 28,
                    Fill = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x38)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var keyText = new TextBlock
                {
                    Text = friendlyKey,
                    FontSize = isTransparent ? 15 : 12,
                    FontWeight = FontWeights.UltraBold,
                    Foreground = new SolidColorBrush(isTransparent
                        ? Color.FromRgb(0x66, 0x77, 0x88)
                        : Color.FromRgb(0x00, 0xE5, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                container.Children.Add(headerText);
                container.Children.Add(separator);
                container.Children.Add(keyText);

                targetButton.Content = container;
                targetButton.ToolTip = isTransparent
                    ? $"Index: {index:D2} | Transparent (Pass-through to lower layer)"
                    : $"Index: {index:D2} | Raw: 0x{keycode:X4}";
            }
        }

        private string GetFriendlyKeyName(ushort keycode)
        {
            return KeycodeDecoder.GetFriendlyKeyName(keycode);
        }

        private async void BtnKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
                await AssignKeyAsync(button);
        }

        private async Task AssignKeyAsync(Button btn)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to your CyberGrip device before assigning keys.",
                                "Device Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (btn.Tag != null && int.TryParse(btn.Tag.ToString(), out int keyIndex))
            {
                byte currentLayer = (byte)(CmbLayer.SelectedIndex >= 0 ? CmbLayer.SelectedIndex : 0);

                var captureWindow = new KeyCaptureWindow(keyIndex)
                {
                    Owner = this
                };

                if (captureWindow.ShowDialog() == true)
                {
                    ushort newKeycode = captureWindow.SelectedKeycode;
                    bool writeSuccess = false;

                    TxtLog.AppendText($"Writing Key {keyIndex:D2} [Layer {currentLayer}] -> 0x{newKeycode:X4} ({KeycodeDecoder.GetFriendlyKeyName(newKeycode)})...\n");

                    await Task.Run(() =>
                    {
                        writeSuccess = _viaCommunicator.WriteLayerKeycode(currentLayer, (byte)keyIndex, newKeycode);
                    });

                    if (writeSuccess)
                    {
                        TxtLog.AppendText($"SUCCESS: Key {keyIndex:D2} saved to EEPROM.\n");
                        UpdateKeyCapLabel(keyIndex, newKeycode);
                    }
                    else
                    {
                        TxtLog.AppendText($"ERROR: Failed to write keycode to Key {keyIndex:D2}.\n");
                        MessageBox.Show($"Failed to write keycode 0x{newKeycode:X4} to hardware on Key {keyIndex:D2}.",
                                        "EEPROM Write Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        #region Profile Save & Load

        private async void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to your CyberGrip device before saving a profile.",
                                "Device Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtStatus.Text = "Status: Reading EEPROM & Exporting JSON...";

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "CyberGrip 18 Profile (*.cg18)|*.cg18",
                DefaultExt = "cg18",
                FileName = $"CyberGrip_Profile_{DateTime.Now:yyyyMMdd_HHmm}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                BtnSaveProfile.IsEnabled = false;
                TxtStatus.Text = "Status: Exporting hardware profile...";

                try
                {
                    byte layerCount = _viaCommunicator.GetLayerCount();
                    if (layerCount == 0) layerCount = 4;

                    var profile = new KeyboardProfile
                    {
                        ProfileName = Path.GetFileNameWithoutExtension(saveFileDialog.FileName),
                        CreatedDate = DateTime.Now,
                        TotalKeys = 19,
                        LayerJoystickInversions = new Dictionary<byte, JoystickInversionProfile>()
                    };

                    await Task.Run(() =>
                    {
                        for (byte l = 0; l < layerCount; l++)
                        {
                            var layerKeycodes = _viaCommunicator.ReadLayerKeycodes(l, 19);
                            profile.Layers[l] = layerKeycodes;

                            if (l < 4)
                            {
                                var inversion = _viaCommunicator.GetJoystickInversion(l);
                                if (inversion.Success)
                                {
                                    profile.LayerJoystickInversions[l] = new JoystickInversionProfile
                                    {
                                        InvertX = inversion.InvertX,
                                        InvertY = inversion.InvertY
                                    };
                                }
                            }
                        }
                    });

                    if (profile.LayerJoystickInversions.TryGetValue(0, out var baseLayerInversion))
                    {
                        // Preserve top-level fields so older profile readers can still
                        // see the base-layer values.
                        profile.InvertX = baseLayerInversion.InvertX;
                        profile.InvertY = baseLayerInversion.InvertY;
                    }

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string jsonString = JsonSerializer.Serialize(profile, options);
                    await File.WriteAllTextAsync(saveFileDialog.FileName, jsonString);

                    TxtLog.AppendText($"SUCCESS: Exported {profile.Layers.Count} layers to '{saveFileDialog.FileName}'.\n");
                    TxtStatus.Text = profile.LayerJoystickInversions.Count == Math.Min(layerCount, (byte)4)
                        ? "Status: Profile Exported Successfully"
                        : "Status: Profile exported; some layer inversion settings were unavailable";
                    if (profile.LayerJoystickInversions.Count != Math.Min(layerCount, (byte)4))
                    {
                        TxtLog.AppendText("NOTE: Some per-layer joystick inversion settings were unavailable from the connected firmware.\n");
                    }
                }
                catch (Exception ex)
                {
                    TxtLog.AppendText($"ERROR: Failed to save profile: {ex.Message}\n");
                    MessageBox.Show($"Failed to save profile:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnSaveProfile.IsEnabled = true;
                }
            }
        }

        private async void BtnLoadProfile_Click(object sender, RoutedEventArgs e)
        {
            await LoadProfileAsync();
        }

        private async Task LoadProfileAsync(string? selectedProfilePath = null)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Please connect to your CyberGrip device before loading a profile.",
                                "Device Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtStatus.Text = "Status: Flashing JSON profile to hardware...";

            if (selectedProfilePath == null)
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "CyberGrip 18 Profile (*.cg18)|*.cg18",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() != true)
                    return;

                selectedProfilePath = openFileDialog.FileName;
            }

            try
            {
                    string jsonString = await File.ReadAllTextAsync(selectedProfilePath);
                    var profile = JsonSerializer.Deserialize<KeyboardProfile>(jsonString);

                    if (profile == null || profile.Layers == null || profile.Layers.Count == 0)
                    {
                        MessageBox.Show("Invalid profile file format.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var result = MessageBox.Show(
                        $"Are you sure you want to flash '{profile.ProfileName}' to hardware?\n" +
                        $"This will overwrite {profile.Layers.Count} layer(s) and any included per-layer joystick settings on your CyberGrip device.",
                        "Confirm Hardware Flash",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes) return;

                    BtnLoadProfile.IsEnabled = false;
                    BtnSaveProfile.IsEnabled = false;
                    TxtStatus.Text = "Status: Flashing profile to EEPROM...";

                    bool allWritesSucceeded = true;
                    int totalFlashedKeys = 0;

                    await Task.Run(() =>
                    {
                        foreach (var layerKvp in profile.Layers)
                        {
                            byte layerIndex = layerKvp.Key;
                            List<ushort> keycodes = layerKvp.Value;

                            for (byte k = 0; k < keycodes.Count && k < 19; k++)
                            {
                                bool success = _viaCommunicator.WriteLayerKeycode(layerIndex, k, keycodes[k]);
                                if (!success)
                                {
                                    allWritesSucceeded = false;
                                }
                                else
                                {
                                    totalFlashedKeys++;
                                }
                            }
                        }
                    });

                    bool hasLayerInversions = profile.LayerJoystickInversions?.Count > 0;
                    bool hasLegacyInversion = profile.InvertX.HasValue || profile.InvertY.HasValue;
                    bool inversionRestored = true;
                    if (hasLayerInversions || hasLegacyInversion)
                    {
                        byte deviceLayerCount = await Task.Run(_viaCommunicator.GetLayerCount);
                        if (deviceLayerCount == 0) deviceLayerCount = 4;
                        deviceLayerCount = Math.Min(deviceLayerCount, (byte)4);

                        inversionRestored = await Task.Run(() =>
                        {
                            bool success = true;
                            if (hasLayerInversions)
                            {
                                foreach (var entry in profile.LayerJoystickInversions!)
                                {
                                    if (entry.Key >= deviceLayerCount ||
                                        !_viaCommunicator.SetJoystickInversion(entry.Key, entry.Value.InvertX, entry.Value.InvertY))
                                    {
                                        success = false;
                                    }
                                }
                            }
                            else
                            {
                                // Older profiles stored one global setting. Apply it to
                                // every layer to preserve their original behavior.
                                for (byte layer = 0; layer < deviceLayerCount; layer++)
                                {
                                    var current = _viaCommunicator.GetJoystickInversion(layer);
                                    if (!current.Success || !_viaCommunicator.SetJoystickInversion(
                                            layer,
                                            profile.InvertX ?? current.InvertX,
                                            profile.InvertY ?? current.InvertY))
                                    {
                                        success = false;
                                    }
                                }
                            }

                            return success;
                        });
                    }

                    if (allWritesSucceeded)
                    {
                        RememberRecentProfile(selectedProfilePath);
                        TxtLog.AppendText($"SUCCESS: Flashed profile '{profile.ProfileName}' ({totalFlashedKeys} keys) to EEPROM.\n");
                        TxtStatus.Text = inversionRestored
                            ? "Status: Profile Flashed Successfully"
                            : "Status: Profile loaded; inversion settings were not restored";

                        if (!inversionRestored)
                        {
                            MessageBox.Show(
                                "The keymap loaded, but some per-layer joystick inversion settings could not be restored. Update the firmware to enable this feature.",
                                "Joystick Settings Not Restored", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        TxtLog.AppendText("WARNING: Profile flash completed with one or more write failures.\n");
                        MessageBox.Show("Profile applied, but some keycodes failed to write to EEPROM.",
                                        "Partial Write Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    byte currentActiveLayer = (byte)(CmbLayer.SelectedIndex >= 0 ? CmbLayer.SelectedIndex : 0);
                    LoadLayerToGUI(currentActiveLayer);
                    await LoadJoystickInversionForLayerAsync(currentActiveLayer);
                }
                catch (Exception ex)
                {
                    TxtLog.AppendText($"ERROR: Failed to load profile: {ex.Message}\n");
                    MessageBox.Show($"Failed to load profile:\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnLoadProfile.IsEnabled = true;
                    BtnSaveProfile.IsEnabled = true;
                }
        }

        #endregion

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            ShowInTaskbar = false;
            _trayIcon.Visible = true;
        }

        private void BtnAdvancedVial_Click(object sender, RoutedEventArgs e)
        {
            if (BtnAdvancedVial.ContextMenu is not ContextMenu menu)
                return;

            menu.PlacementTarget = BtnAdvancedVial;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OpenVialWeb_Click(object sender, RoutedEventArgs e)
        {
            LaunchVial("https://vial.rocks/");
        }

        private void GetVialDesktop_Click(object sender, RoutedEventArgs e)
        {
            LaunchVial("https://get.vial.today/");
        }

        private void LaunchVial(string url)
        {
            try
            {
                _vialExternalToolLaunched = true;
                _deviceChangeTimer.Stop();
                if (_pollingTestRunning)
                    StopPollingRateTest();
                StopMatrixPolling();
                _viaCommunicator.StopMatrixTest();
                _physicalKeyCaptureCancellationToken?.Cancel();

                _isConnected = false;
                ChkInvertX.IsEnabled = false;
                ChkInvertY.IsEnabled = false;
                CmbLayer.IsEnabled = false;
                TxtUsbPollingRate.Text = "--- Hz";
                _usbPollingRateHz = null;
                UpdateMatrixTheoreticalMax();
                SetConnectionIndicator("VIAL ACTIVE", Color.FromRgb(0xFF, 0xC1, 0x07));
                TxtStatus.Text = "Status: Close Vial, then click Connect & Sync to return";

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                LogDebug($"Released CyberGrip HID connection and opened {url}.");
            }
            catch (Exception ex)
            {
                _vialExternalToolLaunched = false;
                SetDisconnectedState("Vial Could Not Open", $"Could not open Vial: {ex.Message}");
                MessageBox.Show($"Could not open the Vial page.\n{ex.Message}",
                    "Could Not Open Vial", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnFlashStockFirmware_Click(object sender, RoutedEventArgs e)
        {
            await FlashFirmwareAsync(useStockFirmware: true, firmwarePath: null);
        }

        private async void BtnFlashFirmwareFromFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select CyberGrip18 Firmware",
                Filter = "RP2040 UF2 firmware (*.uf2)|*.uf2",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
                await FlashFirmwareAsync(useStockFirmware: false, firmwarePath: dialog.FileName);
        }

        private async Task FlashFirmwareAsync(bool useStockFirmware, string? firmwarePath)
        {
            string firmwareName = useStockFirmware
                ? "the included CyberGrip18 stock firmware"
                : Path.GetFileName(firmwarePath) ?? "the selected UF2 file";
            var confirmation = MessageBox.Show(
                $"You are about to flash {firmwareName}.\n\n" +
                "This will replace the keyboard firmware and temporarily disconnect the keyboard. " +
                "Only continue with a UF2 built for the CyberGrip18 Pico. Do not unplug the keyboard while the firmware is being copied. " +
                "If the keyboard does not reconnect afterward, unplug its USB cable and plug it back in.\n\n" +
                "Continue?",
                "Confirm Firmware Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
                return;

            BtnFlashStockFirmware.IsEnabled = false;
            BtnFlashFirmwareFromFile.IsEnabled = false;
            BtnConnect.IsEnabled = false;

            try
            {
                if (!useStockFirmware && (string.IsNullOrWhiteSpace(firmwarePath) || !File.Exists(firmwarePath)))
                    throw new FileNotFoundException("The selected UF2 file could not be found.", firmwarePath);

                string sourcePath = useStockFirmware ? ExtractStockFirmware() : firmwarePath!;
                if (!string.Equals(Path.GetExtension(sourcePath), ".uf2", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Select a .uf2 firmware file for the Pico.");

                DriveInfo? bootDrive = await Task.Run(FindRp2040Drive);
                if (bootDrive == null && _isConnected)
                {
                    StopPollingRateTest();
                    StopMatrixPolling();
                    _viaCommunicator.StopMatrixTest();

                    TxtStatus.Text = "Status: Requesting Pico bootloader...";
                    LogDebug("Firmware update requested; sending the CyberGrip bootloader command.");
                    bool bootloaderAccepted = await Task.Run(_viaCommunicator.RequestBootloader);
                    if (bootloaderAccepted)
                    {
                        TxtStatus.Text = "Status: Waiting for Pico boot drive...";
                        bootDrive = await WaitForRp2040DriveAsync(TimeSpan.FromSeconds(30));
                    }
                    else
                    {
                        LogDebug("The firmware did not acknowledge the app bootloader command; offering manual bootloader entry.");
                        bootDrive = await PromptManualBootloaderAndWaitAsync(keyboardWasConnected: true);
                    }
                }
                else if (bootDrive == null)
                {
                    bootDrive = await PromptManualBootloaderAndWaitAsync(keyboardWasConnected: false);
                }

                if (bootDrive == null)
                {
                    TxtStatus.Text = "Status: Pico boot drive not found";
                    MessageBox.Show(
                        "The RPI-RP2 drive did not appear, so no firmware was copied. Try entering bootloader mode again and make sure Windows detects the Pico drive.",
                        "Pico Drive Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TxtStatus.Text = "Status: Copying firmware to Pico...";
                string destination = Path.Combine(bootDrive.RootDirectory.FullName, Path.GetFileName(sourcePath));
                await Task.Run(() => File.Copy(sourcePath, destination, overwrite: true));
                LogDebug($"Copied firmware '{Path.GetFileName(sourcePath)}' to the Pico boot drive.");

                TxtStatus.Text = "Status: Waiting for keyboard to reconnect...";
                bool reconnected = await WaitForKeyboardReconnectAsync(TimeSpan.FromSeconds(45));
                if (reconnected)
                {
                    TxtStatus.Text = "Status: Firmware update complete";
                    MessageBox.Show("Firmware copied successfully and the CyberGrip18 keyboard reconnected.",
                        "Firmware Update Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TxtStatus.Text = "Status: Firmware copied; waiting for keyboard";
                    MessageBox.Show(
                        "Windows accepted the UF2 file, but the keyboard has not reconnected yet. Give it a little longer; if it remains disconnected, unplug and reconnect it.",
                        "Waiting for Keyboard", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Status: Firmware update failed";
                LogDebug($"Firmware update failed: {ex}");
                MessageBox.Show($"Firmware update failed:\n{ex.Message}",
                    "Firmware Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnFlashStockFirmware.IsEnabled = true;
                BtnFlashFirmwareFromFile.IsEnabled = true;
                BtnConnect.IsEnabled = true;
            }
        }

        private static string ExtractStockFirmware()
        {
            const string resourceSuffix = "Firmware.handwired_cybergrip18_vial.uf2";
            string? resourceName = typeof(MainWindow).Assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
                throw new FileNotFoundException("The stock CyberGrip18 UF2 is missing from this app build.");

            string stockPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyberGrip18", "firmware", "handwired_cybergrip18_vial.uf2");
            Directory.CreateDirectory(Path.GetDirectoryName(stockPath)!);
            using Stream resource = typeof(MainWindow).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException("The stock CyberGrip18 UF2 resource could not be opened.");
            using var destination = File.Create(stockPath);
            resource.CopyTo(destination);
            return stockPath;
        }

        private static async Task<DriveInfo?> WaitForRp2040DriveAsync(TimeSpan timeout)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                DriveInfo? drive = await Task.Run(FindRp2040Drive);
                if (drive != null)
                    return drive;

                await Task.Delay(500);
            }

            return null;
        }

        private static DriveInfo? FindRp2040Drive()
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady && string.Equals(drive.VolumeLabel, "RPI-RP2", StringComparison.OrdinalIgnoreCase))
                        return drive;
                }
                catch (IOException)
                {
                    // A removable drive can disappear while its status is being queried.
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore drives Windows does not allow the current user to inspect.
                }
            }

            return null;
        }

        private async Task<bool> WaitForKeyboardReconnectAsync(TimeSpan timeout)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                if (await Task.Run(_viaCommunicator.IsDevicePresent))
                    return true;

                await Task.Delay(500);
            }

            return false;
        }

        private async Task<DriveInfo?> PromptManualBootloaderAndWaitAsync(bool keyboardWasConnected)
        {
            string entryInstructions = keyboardWasConnected
                ? "The app could not switch the keyboard automatically. Press the key assigned to Bootloader, or unplug the keyboard and hold BOOTSEL on the Pico while plugging it back in."
                : "Unplug the keyboard, press and hold BOOTSEL on the Pico, then plug the keyboard back in and release BOOTSEL.";

            MessageBox.Show(
                $"The keyboard needs to be put into bootloader mode manually.\n\n{entryInstructions}\n\n" +
                "Wait for the RPI-RP2 drive to appear in Windows, then click OK. The app will copy the selected UF2 file automatically.",
                "Enter Pico Bootloader", MessageBoxButton.OK, MessageBoxImage.Information);

            TxtStatus.Text = "Status: Waiting for Pico boot drive...";
            return await WaitForRp2040DriveAsync(TimeSpan.FromSeconds(60));
        }

        private Forms.NotifyIcon CreateTrayIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            var trayIcon = new Forms.NotifyIcon
            {
                Icon = File.Exists(iconPath) ? new Drawing.Icon(iconPath) : Drawing.SystemIcons.Application,
                Text = "CyberGrip 18 Controller",
                Visible = true
            };

            var menu = new Forms.ContextMenuStrip();
            var openItem = new Forms.ToolStripMenuItem("Open CyberGrip 18");
            openItem.Click += (_, _) => RestoreFromTray();
            menu.Items.Add(openItem);

            _loadProfileMenu = new Forms.ToolStripMenuItem("Load Profile");
            var browseItem = new Forms.ToolStripMenuItem("Browse...");
            browseItem.Click += async (_, _) =>
            {
                RestoreFromTray();
                await LoadProfileAsync();
            };
            _loadProfileMenu.DropDownItems.Add(browseItem);
            _loadProfileMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_loadProfileMenu);
            RefreshRecentProfilesMenu();

            menu.Items.Add(new Forms.ToolStripSeparator());
            var exitItem = new Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => Close();
            menu.Items.Add(exitItem);

            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += (_, _) => RestoreFromTray();
            return trayIcon;
        }

        private static string RecentProfilesFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberGrip18",
            "recent-profiles.json");

        private void LoadRecentProfiles()
        {
            try
            {
                string path = RecentProfilesFilePath;
                if (!File.Exists(path))
                    return;

                var savedPaths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
                if (savedPaths == null)
                    return;

                _recentProfiles.AddRange(savedPaths
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5));
            }
            catch
            {
                _recentProfiles.Clear();
            }
        }

        private void RememberRecentProfile(string profilePath)
        {
            _recentProfiles.RemoveAll(path => string.Equals(path, profilePath, StringComparison.OrdinalIgnoreCase));
            _recentProfiles.Insert(0, profilePath);
            if (_recentProfiles.Count > 5)
                _recentProfiles.RemoveRange(5, _recentProfiles.Count - 5);

            try
            {
                string path = RecentProfilesFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(_recentProfiles));
            }
            catch (Exception ex)
            {
                LogDebug($"Could not save recent profiles: {ex.Message}");
            }

            RefreshRecentProfilesMenu();
        }

        private void RefreshRecentProfilesMenu()
        {
            if (_loadProfileMenu == null)
                return;

            while (_loadProfileMenu.DropDownItems.Count > 2)
                _loadProfileMenu.DropDownItems.RemoveAt(2);

            var existingProfiles = _recentProfiles.Where(File.Exists).ToList();
            if (existingProfiles.Count == 0)
            {
                _loadProfileMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("No recent profiles") { Enabled = false });
                return;
            }

            foreach (string profilePath in existingProfiles)
            {
                var item = new Forms.ToolStripMenuItem(Path.GetFileNameWithoutExtension(profilePath))
                {
                    ToolTipText = profilePath
                };
                item.Click += async (_, _) =>
                {
                    RestoreFromTray();
                    await LoadProfileAsync(profilePath);
                };
                _loadProfileMenu.DropDownItems.Add(item);
            }
        }

        internal void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void SetKeyIllumination(int index, bool isPressed)
        {
            // Ensure UI updates happen on the dispatcher thread
            Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ILLUMINATION: index={index}, pressed={isPressed}");

                var btn = GetKeyButton(index, testing: true);
                if (btn != null)
                {
                    if (isPressed)
                    {
                        // Illuminated / Pressed State (Bright Cyan Glow)
                        btn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF));
                        btn.BorderBrush = new SolidColorBrush(Colors.White);
                        btn.BorderThickness = new Thickness(2);
                    }
                    else
                    {
                        // Default / Released State (Dark Theme)
                        btn.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x28));
                        btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44));
                        btn.BorderThickness = new Thickness(1);
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"UI: BtnTestKey{index:D2} Background={btn.Background}");

                }
            });
        }

        private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabs)
                return;

            if (MainTabs.SelectedIndex == 1)
            {
                if (_physicalKeyCaptureRunning)
                {
                    _physicalKeyCaptureCancellationToken?.Cancel();
                    if (_physicalKeyCaptureTask != null)
                        await WaitForPhysicalKeyCaptureAsync(_physicalKeyCaptureTask);
                }
                if (!_pollingTestRunning)
                    StartMatrixPolling();
            }
            else
            {
                _physicalKeyCaptureCancellationToken?.Cancel();
                StopPollingRateTest();
                StopMatrixPolling();
            }
        }

        private static async Task WaitForPhysicalKeyCaptureAsync(Task captureTask)
        {
            try
            {
                await captureTask;
            }
            catch
            {
                // Capture cancellation is expected when changing tabs.
            }
        }

        private async void BtnCapturePhysicalKey_Click(object sender, RoutedEventArgs e)
        {
            if (_physicalKeyCaptureRunning)
            {
                _physicalKeyCaptureCancellationToken?.Cancel();
                TxtPhysicalKeyCaptureStatus.Text = "Cancelling key selection…";
                return;
            }

            if (!_isConnected)
            {
                TxtPhysicalKeyCaptureStatus.Text = "Connect the keyboard first.";
                return;
            }

            if (MainTabs.SelectedIndex != 0)
                return;

            if (!_viaCommunicator.StartMatrixTest())
            {
                TxtPhysicalKeyCaptureStatus.Text = "Could not listen to the keyboard.";
                return;
            }

            _physicalKeyCaptureCancellationToken = new CancellationTokenSource();
            CancellationToken token = _physicalKeyCaptureCancellationToken.Token;
            _physicalKeyCaptureRunning = true;
            BtnCapturePhysicalKey.Content = "Cancel Key Selection";
            TxtPhysicalKeyCaptureStatus.Text = "Press the key you want to remap.";

            Task captureTask = CapturePhysicalKeyAsync(token);
            _physicalKeyCaptureTask = captureTask;
            try
            {
                await captureTask;
            }
            finally
            {
                if (ReferenceEquals(_physicalKeyCaptureTask, captureTask))
                    _physicalKeyCaptureTask = null;
                _physicalKeyCaptureCancellationToken?.Dispose();
                _physicalKeyCaptureCancellationToken = null;
            }
        }

        private async Task CapturePhysicalKeyAsync(CancellationToken token)
        {
            int selectedKey = -1;
            bool timedOut = false;

            try
            {
                selectedKey = await Task.Run(() =>
                {
                    bool[] previousStates = _viaCommunicator.GetMatrixStates(19);
                    var timer = Stopwatch.StartNew();

                    while (!token.IsCancellationRequested && timer.Elapsed < TimeSpan.FromSeconds(10))
                    {
                        bool[] currentStates = _viaCommunicator.GetMatrixStates(19);
                        for (int keyIndex = 0; keyIndex < currentStates.Length; keyIndex++)
                        {
                            if (currentStates[keyIndex] && !previousStates[keyIndex])
                                return keyIndex;
                        }

                        previousStates = currentStates;
                        Thread.Sleep(30);
                    }

                    return -1;
                }, token);
                timedOut = selectedKey < 0 && !token.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                // User canceled or switched tabs.
            }
            finally
            {
                _viaCommunicator.StopMatrixTest();
                _physicalKeyCaptureRunning = false;
                BtnCapturePhysicalKey.Content = "Select Key on CyberGrip";
            }

            if (selectedKey >= 0 && _isConnected && MainTabs.SelectedIndex == 0)
            {
                TxtPhysicalKeyCaptureStatus.Text = $"Key {selectedKey:D2} selected.";
                Button? keyButton = GetKeyButton(selectedKey, testing: false);
                if (keyButton != null)
                    await AssignKeyAsync(keyButton);
            }
            else if (timedOut)
            {
                TxtPhysicalKeyCaptureStatus.Text = "No key selected. Try again when ready.";
            }
            else
            {
                TxtPhysicalKeyCaptureStatus.Text = string.Empty;
            }
        }

        private CancellationTokenSource? _testPollingCancellationToken;
        private Task? _matrixPollingTask;
        private readonly bool[] _lastPressedStates = new bool[19];

        private CancellationTokenSource? _pollingTestCancellationToken;
        private Task? _pollingTestTask;
        private bool _pollingTestRunning;

        private void StartMatrixPolling()
        {
            // Make absolutely sure an old polling session is gone first.
            StopMatrixPolling();

            if (!_isConnected)
                return;

            if (!_viaCommunicator.StartMatrixTest())
            {
                System.Diagnostics.Debug.WriteLine("Matrix tester: failed to open HID device.");
                return;
            }

            _testPollingCancellationToken = new CancellationTokenSource();
            var token = _testPollingCancellationToken.Token;

            _matrixPollingTask = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested && _isConnected)
                    {
                        bool[] currentStates =
                            _viaCommunicator.GetMatrixStates(19);

                        var joystickAxes = _viaCommunicator.GetJoystickAxes();
                        if (joystickAxes.Success)
                            SetJoystickAxisDisplay(joystickAxes.X, joystickAxes.Y);
                        else
                            SetJoystickTelemetryUnavailable();

                        for (int i = 0; i < currentStates.Length; i++)
                        {
                            if (_lastPressedStates[i] != currentStates[i])
                            {
                                _lastPressedStates[i] = currentStates[i];
                                SetKeyIllumination(i, currentStates[i]);
                            }
                        }

                        await Task.Delay(30, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal when leaving the Testing tab.
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Matrix polling stopped: {ex.GetType().Name}: {ex.Message}");
                }
            }, token);
        }

        private void SetJoystickAxisDisplay(short x, short y)
        {
            Dispatcher.Invoke(() =>
            {
                const double radius = 38;
                const double dotRadius = 6;
                // QMK joystick defaults to 8-bit signed axis reports (-127..127).
                double normalizedX = Math.Clamp(x / 127.0, -1.0, 1.0);
                double normalizedY = Math.Clamp(y / 127.0, -1.0, 1.0);
                Canvas.SetLeft(JoystickPositionDot, 50 + (normalizedX * radius) - dotRadius);
                Canvas.SetTop(JoystickPositionDot, 50 - (normalizedY * radius) - dotRadius);
                TxtJoystickAxes.Text = $"X: {x:+0;-0;0}   Y: {y:+0;-0;0}";
                TxtJoystickTelemetryStatus.Text = "Live axis output";
                TxtJoystickTelemetryStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
            });
        }

        private void SetJoystickTelemetryUnavailable()
        {
            Dispatcher.Invoke(() =>
            {
                const string unavailableMessage = "Flash firmware with joystick telemetry";
                if (TxtJoystickTelemetryStatus.Text == unavailableMessage)
                    return;

                Canvas.SetLeft(JoystickPositionDot, 44);
                Canvas.SetTop(JoystickPositionDot, 44);
                TxtJoystickAxes.Text = "X: ---   Y: ---";
                TxtJoystickTelemetryStatus.Text = unavailableMessage;
                TxtJoystickTelemetryStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
            });
        }

        private void StopMatrixPolling()
        {
            try
            {
                _testPollingCancellationToken?.Cancel();

                // Wait for the polling loop to actually stop
                // before closing the HID stream.
                if (_matrixPollingTask != null &&
                    !_matrixPollingTask.IsCompleted)
                {
                    try
                    {
                        _matrixPollingTask.Wait(500);
                    }
                    catch (AggregateException)
                    {
                        // Ignore cancellation exceptions.
                    }
                }
            }
            catch
            {
                // Ignore shutdown errors.
            }
            finally
            {
                _matrixPollingTask = null;

                _testPollingCancellationToken?.Dispose();
                _testPollingCancellationToken = null;

                // Now it is safe to close the HID stream.
                _viaCommunicator.StopMatrixTest();

                // Clear all test-key illumination.
                for (int i = 0; i < _lastPressedStates.Length; i++)
                {
                    _lastPressedStates[i] = false;
                    SetKeyIllumination(i, false);
                }
            }
        }

        private void BtnPollingTest_Click(object sender, RoutedEventArgs e)
        {
            if (_pollingTestRunning)
            {
                LogDebug("Matrix read test stop requested by user.");
                StopPollingRateTest();
            }
            else
            {
                StartPollingRateTest();
            }
        }

        private void StartPollingRateTest()
        {
            if (!_isConnected)
            {
                MessageBox.Show(
                    "Please connect to your CyberGrip device before running the polling test.",
                    "Device Not Connected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // Don't allow the matrix visual tester to run at the same time.
            StopMatrixPolling();

            int durationSeconds = 15;

            if (CmbPollingDuration.SelectedItem is ComboBoxItem selectedItem)
            {
                string text = selectedItem.Content?.ToString() ?? "15 sec";

                if (int.TryParse(
                    text.Replace(" sec", ""),
                    out int parsedDuration))
                {
                    durationSeconds = parsedDuration;
                }
            }

            if (!_viaCommunicator.StartMatrixTest())
            {
                LogDebug("Matrix read test could not start: failed to open the HID stream.");
                TxtPollingStatus.Text = "HID ERROR";
                TxtPollingStatus.Foreground =
                    new SolidColorBrush(Color.FromRgb(0xFF, 0x66, 0x88));

                return;
            }

            _pollingTestCancellationToken = new CancellationTokenSource();
            CancellationToken token = _pollingTestCancellationToken.Token;

            _pollingTestRunning = true;
            const int warmupSamples = 100;

            LogDebug($"Matrix read test started for {durationSeconds} seconds; ignoring the first {warmupSamples} successful reads during warmup.");

            BtnPollingTest.Content = "STOP TEST";
            CmbPollingDuration.IsEnabled = false;

            TxtPollingStatus.Text = "TESTING...";
            TxtPollingStatus.Foreground =
                new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF));

            TxtPollingRate.Text = "---";
            TxtPollingAverage.Text = "--- ms";
            TxtPollingMinimum.Text = "--- ms";
            TxtPollingMaximum.Text = "--- ms";
            TxtPollingP99.Text = "--- ms";
            TxtPollingSamples.Text = "0";
            TxtPollingFailures.Text = "0";

            _pollingTestTask = Task.Run(async () =>
            {
                var intervals = new List<double>();
                int failures = 0;

                Stopwatch stopwatch = Stopwatch.StartNew();
                long previousTimestamp = 0;
                int successfulSamples = 0;

                while (!token.IsCancellationRequested &&
                       stopwatch.Elapsed.TotalSeconds < durationSeconds)
                {
                    Stopwatch sampleTimer = Stopwatch.StartNew();

                    var result = _viaCommunicator.GetMatrixStatesWithResult(19);

                    sampleTimer.Stop();

                    if (result.Success)
                    {
                        successfulSamples++;

                        long now = Stopwatch.GetTimestamp();

                        // Ignore the first few successful reads while
                        // the USB/HID connection settles.
                        if (successfulSamples <= warmupSamples)
                        {
                            previousTimestamp = now;
                        }
                        else
                        {
                            double intervalMs =
                                (now - previousTimestamp) *
                                1000.0 /
                                Stopwatch.Frequency;

                            if (intervalMs > 0)
                                intervals.Add(intervalMs);

                            previousTimestamp = now;
                        }
                    }
                    else
                    {
                        failures++;
                        if (failures == 1 || failures % 100 == 0)
                            LogDebug($"Matrix read failed ({failures} total so far). Further failures are summarized at 100-read intervals.");
                    }

                    await Task.Yield();

                    if (intervals.Count % 50 == 0)
                    {
                        UpdatePollingDisplay(
                            intervals,
                            failures,
                            stopwatch.Elapsed.TotalSeconds);
                    }
                }

                UpdatePollingDisplay(
                    intervals,
                    failures,
                    stopwatch.Elapsed.TotalSeconds);

                string outcome = token.IsCancellationRequested ? "cancelled" : "completed";
                if (intervals.Count > 0)
                {
                    double averageInterval = intervals.Average();
                    double[] sortedIntervals = intervals.ToArray();
                    Array.Sort(sortedIntervals);
                    int p99Index = Math.Max(0, (int)Math.Ceiling(sortedIntervals.Length * 0.99) - 1);
                    double p99Interval = sortedIntervals[p99Index];
                    LogDebug($"Matrix test {outcome}: {successfulSamples:N0} successful reads, {intervals.Count:N0} measured intervals, {failures:N0} failed reads; average {averageInterval:N2} ms ({1000.0 / averageInterval:N1} reads/s), 99th percentile {p99Interval:N2} ms.");
                }
                else
                {
                    LogDebug($"Matrix test {outcome} with no measured intervals; successful reads={successfulSamples}, failures={failures}.");
                }

                Dispatcher.Invoke(() =>
                {
                    if (!_pollingTestRunning)
                        return;

                    _pollingTestRunning = false;

                    BtnPollingTest.Content = "START TEST";
                    CmbPollingDuration.IsEnabled = true;

                    TxtPollingStatus.Text = "TEST COMPLETE";
                    TxtPollingStatus.Foreground =
                        new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x66));
                });

                _viaCommunicator.StopMatrixTest();

                Dispatcher.Invoke(() =>
                {
                    RestartMatrixPollingIfTestingTab();
                });

            }, token);
        }

        private void UpdatePollingDisplay(
            List<double> intervals,
            int failures,
            double elapsedSeconds)
        {
            if (intervals.Count == 0)
                return;

            double average = intervals.Average();
            double minimum = intervals.Min();
            double maximum = intervals.Max();
            double[] sortedIntervals = intervals.ToArray();
            Array.Sort(sortedIntervals);
            int p99Index = Math.Max(0, (int)Math.Ceiling(sortedIntervals.Length * 0.99) - 1);
            double percentile99 = sortedIntervals[p99Index];

            double rate = 1000.0 / average;

            Dispatcher.Invoke(() =>
            {
                TxtPollingRate.Text = $"{rate:N1}";
                TxtPollingAverage.Text = $"{average:N2} ms";
                TxtPollingMinimum.Text = $"{minimum:N2} ms";
                TxtPollingMaximum.Text = $"{maximum:N2} ms";
                TxtPollingP99.Text = $"{percentile99:N2} ms";
                TxtPollingSamples.Text = intervals.Count.ToString("N0");
                TxtPollingFailures.Text = failures.ToString("N0");
            });
        }

        private void UpdateMatrixTheoreticalMax()
        {
            TxtMatrixTheoreticalMax.Text = _usbPollingRateHz.HasValue
                ? $"Request/response theoretical max: {(_usbPollingRateHz.Value / 2.0):N0} reads/s"
                : "Request/response theoretical max: --- reads/s";
        }

        private void StopPollingRateTest()
        {
            try
            {
                _pollingTestCancellationToken?.Cancel();

                if (_pollingTestTask != null &&
                    !_pollingTestTask.IsCompleted)
                {
                    try
                    {
                        _pollingTestTask.Wait(500);
                    }
                    catch (AggregateException)
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _pollingTestTask = null;

                _pollingTestCancellationToken?.Dispose();
                _pollingTestCancellationToken = null;

                _pollingTestRunning = false;

                _viaCommunicator.StopMatrixTest();

                Dispatcher.Invoke(() =>
                {
                    BtnPollingTest.Content = "START TEST";
                    CmbPollingDuration.IsEnabled = true;
                    TxtPollingStatus.Text = "STOPPED";
                    TxtPollingStatus.Foreground =
                        new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99));
                    RestartMatrixPollingIfTestingTab();
                });
            }
        }

        private void RestartMatrixPollingIfTestingTab()
        {
            if (_isConnected && MainTabs.SelectedIndex == 1 && !_pollingTestRunning)
            {
                StartMatrixPolling();
            }
        }

    }
}
