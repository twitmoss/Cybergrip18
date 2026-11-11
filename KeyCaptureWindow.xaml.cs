using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CyberGrip
{
    public partial class KeyCaptureWindow : Window
    {
        private ushort _baseKeycode = 0x0000;
        private bool _isUpdatingUi = false;

        public ushort SelectedKeycode { get; private set; }
        public bool IsKeyAssigned { get; private set; }

        public KeyCaptureWindow() : this(0) { }

        public KeyCaptureWindow(int keyIndex)
        {
            InitializeComponent();
            TxtHeader.Text = $"Assign Key #{keyIndex:D2}";

            PreviewKeyDown += KeyCaptureWindow_PreviewKeyDown;
        }

        private void KeyCaptureWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Only process raw key presses if the user is on the Physical Key tab (Tab 0)
            if (MainTabControl.SelectedIndex != 0) return;

            // Allow normal combo dropdown navigation with arrow keys / tab
            if (e.Key == Key.Tab && (Keyboard.FocusedElement is ComboBox || Keyboard.FocusedElement is Button))
                return;

            Key realKey = (e.Key == Key.System) ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;

            bool isModifierKeyOnly = realKey == Key.LeftShift || realKey == Key.RightShift ||
                                     realKey == Key.LeftCtrl || realKey == Key.RightCtrl ||
                                     realKey == Key.LeftAlt || realKey == Key.RightAlt ||
                                     realKey == Key.LWin || realKey == Key.RWin;

            if (isModifierKeyOnly)
            {
                _baseKeycode = QmkKeyMap.GetQmkCodeFromWpfKey(realKey);
            }
            else
            {
                ushort qmkCode = QmkKeyMap.GetQmkCodeFromWpfKey(realKey);
                if (qmkCode != 0)
                {
                    _baseKeycode = qmkCode;
                }
            }

            _isUpdatingUi = true;
            ChkShift.IsChecked = mods.HasFlag(ModifierKeys.Shift);
            ChkCtrl.IsChecked = mods.HasFlag(ModifierKeys.Control);
            ChkAlt.IsChecked = mods.HasFlag(ModifierKeys.Alt);
            ChkGui.IsChecked = mods.HasFlag(ModifierKeys.Windows);
            _isUpdatingUi = false;

            CalculateAndDisplayKeycode();
            e.Handled = true;
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only handle tab changes, not dropdown selection changes propagating up
            if (e.Source is TabControl)
            {
                SyncActiveTabKeycode();
            }
        }

        private void SyncActiveTabKeycode()
        {
            if (MainTabControl == null) return;

            switch (MainTabControl.SelectedIndex)
            {
                case 0: // Physical Key Tab
                    CalculateAndDisplayKeycode();
                    break;
                case 1: // VIA Layers Tab
                    UpdateLayerKeycode();
                    break;
                case 2: // Media Tab
                    UpdateMediaKeycode();
                    break;
            }
        }

        private void ManualModifier_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingUi && MainTabControl.SelectedIndex == 0)
            {
                CalculateAndDisplayKeycode();
            }
        }

        private void LayerSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabControl?.SelectedIndex == 1)
            {
                UpdateLayerKeycode();
            }
        }

        private void MediaSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabControl?.SelectedIndex == 2)
            {
                UpdateMediaKeycode();
            }
        }

        private void CalculateAndDisplayKeycode()
        {
            ushort code = _baseKeycode;

            if (ChkCtrl.IsChecked == true && _baseKeycode != 0x00E0) code |= 0x0100;
            if (ChkShift.IsChecked == true && _baseKeycode != 0x00E1) code |= 0x0200;
            if (ChkAlt.IsChecked == true && _baseKeycode != 0x00E2) code |= 0x0400;
            if (ChkGui.IsChecked == true && _baseKeycode != 0x00E3) code |= 0x0800;

            SelectedKeycode = code;
            TxtDetectedKey.Text = QmkKeyMap.GetStringFromQmkCode(SelectedKeycode);
        }

        private void UpdateLayerKeycode()
        {
            int mode = CmbLayerMode.SelectedIndex;
            int targetLayer = CmbTargetLayer.SelectedIndex + 1;

            SelectedKeycode = mode switch
            {
                0 => (ushort)(0x5200 + targetLayer), // MO(layer)
                1 => (ushort)(0x5250 + targetLayer), // TG(layer)
                2 => (ushort)(0x5220 + targetLayer), // TO(layer)
                _ => 0x0000
            };

            TxtDetectedKey.Text = QmkKeyMap.GetStringFromQmkCode(SelectedKeycode);
        }

        private void UpdateMediaKeycode()
        {
            int mediaIdx = CmbMediaAction.SelectedIndex;

            SelectedKeycode = mediaIdx switch
            {
                0 => 0x00A8, // KC_MUTE
                1 => 0x00A9, // KC_VOLU
                2 => 0x00AA, // KC_VOLD
                3 => 0x00AE, // KC_MPLY
                4 => 0x00AB, // KC_MNXT
                5 => 0x00AC, // KC_MPRV
                _ => 0x0000
            };

            TxtDetectedKey.Text = QmkKeyMap.GetStringFromQmkCode(SelectedKeycode);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _baseKeycode = 0x0000;
            SelectedKeycode = 0x0000;

            _isUpdatingUi = true;
            ChkShift.IsChecked = false;
            ChkCtrl.IsChecked = false;
            ChkAlt.IsChecked = false;
            ChkGui.IsChecked = false;
            _isUpdatingUi = false;

            TxtDetectedKey.Text = "KC_NO (Clear)";
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            IsKeyAssigned = true;
            DialogResult = true;
        }

        private void BtnSetTransparent_Click(object sender, RoutedEventArgs e)
        {
            SelectedKeycode = 0x0001; // KC_TRNS
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsKeyAssigned = false;
            DialogResult = false;
        }
    }
}