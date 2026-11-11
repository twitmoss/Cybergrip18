# CyberGrip18

CyberGrip18 is a Windows companion app for the CyberGrip18 keyboard. It provides quick access to key remapping, joystick settings and testing, profiles, and firmware updates. Use Vial for advanced keyboard configuration.

## Getting started

Run `CyberGrip18-Setup.exe` to install the app for your Windows account. Setup adds Start menu and desktop shortcuts; administrator access is not required.
CyberGrip18 also starts automatically when you sign in to Windows. To disable that, remove **CyberGrip18** from **Settings → Apps → Startup**.

1. Connect the CyberGrip18 keyboard to your PC and open the app.
2. Select **Connect & Sync** if the app has not connected automatically.
3. Close Vial before connecting with CyberGrip18. Only one app can communicate with the keyboard at a time.

The keyboard should show as connected in the app. If it does not, reconnect the USB cable and select **Connect & Sync** again.

## Programming

- Choose a layer from **Selected Layer** to view and edit that layer.
- Select a key in the app, then choose the key assignment. **Select Key on CyberGrip** lets you identify a physical key by pressing it; that press also goes to the game or app currently receiving keyboard input.
- Key assignments are written to the keyboard as you change them.
- Use **Save Profile** to back up the keyboard to a `.cg18` file. **Load Profile** writes a saved profile onto the connected keyboard. Loading a profile replaces the saved mappings and included joystick inversion settings, so confirm the prompt before proceeding.
- The joystick X and Y inversion settings are saved on the keyboard separately for each layer.
- The layer shortcut works from any layer: hold **Pinky Extra**, **Pinky Press Down**, and **Ring Press Down**, then press **Pinky High** for Layer 0, **Ring High** for Layer 1, **Middle High** for Layer 2, or **Index High** for Layer 3. The shortcut keys still send their normal keystrokes to the game or app.
- Choose **Advanced: Vial** for Vial Web or the Vial desktop app.

## Testing

The **Testing** tab displays the matrix and joystick input and provides the USB polling test. Connect the keyboard before starting a test. The console can be opened for diagnostic messages.

## Firmware updates

Firmware updates replace the keyboard firmware. Export a profile first if you want a backup. Use **Flash Stock Firmware** for the firmware included with this app, or **Flash UF2 from File** for a CyberGrip18 firmware file you have built or obtained. Only flash a UF2 made for the CyberGrip18 Pico.

Follow the confirmation and on-screen instructions. If the app cannot switch the keyboard to bootloader mode, enter it manually by holding **BOOTSEL** on the Pico while connecting the keyboard. When Windows shows the **RPI-RP2** drive, return to the app and continue; it will copy the firmware. Do not unplug the keyboard while the file is being copied. The keyboard will restart afterward. If it does not reconnect, unplug and reconnect its USB cable.

## System tray

Clicking the window's **X** hides CyberGrip18 in the system tray. Double-click the tray icon to reopen it, or right-click the icon to load a profile or exit the app.

To remove the app, open **Settings → Apps → Installed apps**, select **CyberGrip18**, and choose **Uninstall**.
