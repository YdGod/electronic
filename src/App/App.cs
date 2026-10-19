using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BatteryPet
{
    internal sealed class PetWindow : Window
    {
        internal static readonly uint ShowMessage = RegisterWindowMessage("BatteryPet.ShowDevices.v1");
        internal static readonly uint RestoreMessage = RegisterWindowMessage("BatteryPet.RestorePet.v1");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr handle, uint message, IntPtr wparam, IntPtr lparam);
        private readonly Settings settings;
        private readonly PetView pet;
        private readonly BubbleView bubble;
        private readonly Window popup;
        private readonly Forms.NotifyIcon tray;
        private MenuItem skinMenu;
        private ThermalSnapshot thermal;
        private CancellationTokenSource queryCancellation;
        private int popupGeneration;
        private bool refreshQueued;
        private ScanResult result;
        private bool scanning, dragging, exiting;
        private Point? pressed;
        private string scanError;
        internal PetWindow(bool openDevices)
        {
            settings = Settings.Load();
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = settings.Topmost; Title = "小电量管家";
            pet = new PetView(settings.Skin); pet.HideRequested = HidePet; var view = new Viewbox { Child = pet, Stretch = Stretch.Uniform }; Content = view; ApplySize(settings.Size);
            Left = settings.PositionSaved ? settings.Left : SystemParameters.WorkArea.Right - Width - 45;
            Top = settings.PositionSaved ? settings.Top : SystemParameters.WorkArea.Bottom - Height - 25;
            bubble = new BubbleView(); bubble.RefreshRequested = Refresh; bubble.CloseRequested = delegate { popup.Hide(); }; bubble.HideRequested = HidePet;
            bubble.SkinSelected = SelectSkin; bubble.SetSkin(settings.Skin);
            popup = new Window { Content = bubble, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = settings.Topmost, SizeToContent = SizeToContent.WidthAndHeight, Title = "设备电量" };
            Action hideIfInactive = delegate { if (popup.IsVisible && !popup.IsActive && !popup.IsMouseOver && !bubble.IsSkinMenuOpen) popup.Hide(); };
            // Let the context menu finish opening before checking activation:
            // WPF moves focus before the menu's IsOpen value becomes visible.
            popup.Deactivated += delegate { AppLog.Trace("弹窗失去焦点 " + popup.IsMouseOver); Dispatcher.BeginInvoke(hideIfInactive, DispatcherPriority.Background); };
            bubble.SkinMenuClosed = delegate { Dispatcher.BeginInvoke(hideIfInactive, DispatcherPriority.Background); };
            popup.IsVisibleChanged += delegate { pet.SetPopupVisible(popup.IsVisible); if (!popup.IsVisible) { bubble.CloseSkinMenu(); popupGeneration++; refreshQueued = false; if (queryCancellation != null) queryCancellation.Cancel(); UpdateState(); } };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { pressed = e.GetPosition(this); dragging = false; AppLog.Trace("按下 " + pressed + " 捕获 " + CaptureMouse()); e.Handled = true; };
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += delegate { bool clicked = pressed.HasValue && !dragging; AppLog.Trace("松开 点击=" + clicked + " 位置=" + pressed + " 拖动=" + dragging); ReleaseMouseCapture(); if (clicked) ToggleBubble(); pressed = null; };
            ContextMenu = MakeMenu();
            tray = new Forms.NotifyIcon { Text = "小电量管家 · 查看外设电量和 CPU / 显卡温度", Icon = TrayIcon(), Visible = true };
            var trayMenu = new Forms.ContextMenuStrip(); trayMenu.Items.Add("显示小人", null, delegate { Dispatcher.Invoke(new Action(BringPet)); }); trayMenu.Items.Add("隐藏小人", null, delegate { Dispatcher.Invoke(new Action(HidePet)); }); trayMenu.Items.Add("查看电量与温度", null, delegate { Dispatcher.Invoke(new Action(ShowDevices)); }); trayMenu.Items.Add("退出", null, delegate { Dispatcher.Invoke(new Action(Exit)); }); tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(BringPet)); };
            Loaded += delegate { popup.Owner = this; ClampPosition(); SavePosition(); };
            Closing += delegate { exiting = true; if (queryCancellation != null) queryCancellation.Cancel(); SavePosition(); popup.Close(); tray.Visible = false; tray.Icon.Dispose(); tray.Dispose(); };
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Closed += delegate { SystemEvents.DisplaySettingsChanged -= DisplayChanged; Application.Current.Shutdown(); };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) popup.Hide(); };
            SourceInitialized += delegate
            {
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(delegate(IntPtr handle, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
                {
                    if ((uint)message == ShowMessage) { handled = true; Dispatcher.BeginInvoke(new Action(ShowDevices)); }
                    if ((uint)message == RestoreMessage) { handled = true; Dispatcher.BeginInvoke(new Action(BringPet)); }
                    return IntPtr.Zero;
                });
            };
            if (openDevices) Loaded += delegate { Dispatcher.BeginInvoke(new Action(ToggleBubble)); };
        }
        private static System.Drawing.Icon TrayIcon()
        {
            using (var bitmap = new System.Drawing.Bitmap(32, 32))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(System.Drawing.Color.Transparent);
                using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(110, 102, 175))) graphics.FillEllipse(brush, 1, 1, 30, 30);
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2)) { graphics.DrawRectangle(pen, 7, 10, 16, 12); graphics.DrawLine(pen, 25, 13, 25, 19); }
                var handle = bitmap.GetHicon();
                try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private void DisplayChanged(object sender, EventArgs e) { Dispatcher.BeginInvoke(new Action(delegate { ClampPosition(); SavePosition(); if (popup.IsVisible) PlacePopup(); })); }
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!pressed.HasValue || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(this);
            AppLog.Trace("移动 " + point + " 起点=" + pressed);
            if (Math.Abs(point.X - pressed.Value.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - pressed.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragging = true; ReleaseMouseCapture(); popup.Hide();
            try { DragMove(); }
            catch (InvalidOperationException) { }
            finally { pressed = null; ClampPosition(); SavePosition(); }
        }
        private ContextMenu MakeMenu()
        {
            var menu = new ContextMenu();
            var skins = skinMenu = new MenuItem { Header = "小人皮肤" };
            foreach (var available in PetSkins.All)
            {
                var selected = available;
                var item = new MenuItem { Header = selected.Name, Tag = selected.Id, IsCheckable = true, IsChecked = selected.Id == settings.Skin };
                item.Click += delegate { SelectSkin(selected.Id); };
                skins.Items.Add(item);
            }
            menu.Items.Add(skins);
            var open = new MenuItem { Header = "查看电量与温度" }; open.Click += delegate { ToggleBubble(); }; menu.Items.Add(open);
            var hide = new MenuItem { Header = "隐藏小人（双击托盘图标恢复）" }; hide.Click += delegate { HidePet(); }; menu.Items.Add(hide); menu.Items.Add(new Separator());
            var top = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = settings.Topmost }; top.Click += delegate { Topmost = popup.Topmost = settings.Topmost = top.IsChecked; settings.Save(); }; menu.Items.Add(top);
            var sizes = new MenuItem { Header = "小人大小" };
            foreach (var size in new[] { 160.0, 210.0, 280.0 })
            {
                double value = size; var item = new MenuItem { Header = size == 160 ? "小" : size == 210 ? "中" : "大", IsCheckable = true, IsChecked = settings.Size == size };
                item.Click += delegate { ApplySize(value); foreach (MenuItem sibling in sizes.Items) sibling.IsChecked = sibling == item; ClampPosition(); SavePosition(); }; sizes.Items.Add(item);
            }
            menu.Items.Add(sizes);
            var usb = new MenuItem { Header = "显示 USB 外设候选项", IsCheckable = true, IsChecked = settings.IncludeUsb }; usb.Click += delegate { settings.IncludeUsb = usb.IsChecked; settings.Save(); if (scanning) { result = null; scanError = "本次读取结束后按新设置刷新。"; UpdateState(); } else Refresh(); }; menu.Items.Add(usb);
            var startup = new MenuItem { Header = "开机启动", IsCheckable = true, IsChecked = StartupEnabled() }; startup.Click += delegate
            {
                try { using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) { if (startup.IsChecked) key.SetValue("BatteryPet", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\""); else key.DeleteValue("BatteryPet", false); } }
                catch (Exception ex) { startup.IsChecked = StartupEnabled(); MessageBox.Show("设置开机启动失败：" + ex.Message, "小电量管家"); }
            }; menu.Items.Add(startup);
            var reset = new MenuItem { Header = "放回主屏幕右下角" }; reset.Click += delegate { Left = SystemParameters.WorkArea.Right - Width - 45; Top = SystemParameters.WorkArea.Bottom - Height - 25; SavePosition(); }; menu.Items.Add(reset);
            var export = new MenuItem { Header = "导出设备诊断" }; export.Click += delegate { Export(); }; menu.Items.Add(export); menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "退出挂件" }; exit.Click += delegate { Exit(); }; menu.Items.Add(exit); return menu;
        }
        private void SelectSkin(string id)
        {
            var selected = PetSkins.Find(id);
            settings.Skin = selected.Id; pet.SetSkin(selected.Id); bubble.SetSkin(selected.Id);
            foreach (MenuItem item in skinMenu.Items) item.IsChecked = (string)item.Tag == selected.Id;
            settings.Save();
            if (popup.IsVisible) popup.Activate();
        }
        private static bool StartupEnabled()
        {
            try { using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) return key != null && key.GetValue("BatteryPet") != null; }
            catch { return false; }
        }
        private void ApplySize(double size) { settings.Size = size; Width = size; Height = size * 236 / 210; }
        private void SavePosition() { settings.Left = Left; settings.Top = Top; settings.PositionSaved = true; settings.Save(); }
        private Rect WorkingArea()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var area = hwnd == IntPtr.Zero ? Forms.Screen.PrimaryScreen.WorkingArea : Forms.Screen.FromHandle(hwnd).WorkingArea;
            var source = PresentationSource.FromVisual(this);
            var transform = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
            var start = transform.Transform(new Point(area.Left, area.Top)); var end = transform.Transform(new Point(area.Right, area.Bottom)); return new Rect(start, end);
        }
        private void ClampPosition() { var area = WorkingArea(); Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width)); Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)); }
        private void ToggleBubble()
        {
            if (popup.IsVisible) { popup.Hide(); return; }
            popupGeneration++; popup.Show(); PlacePopup(); popup.Activate(); Refresh();
            AppLog.Trace("弹窗打开 Visible=" + popup.IsVisible);
        }
        private void ShowDevices() { BringPet(); if (!popup.IsVisible) ToggleBubble(); else { popup.Activate(); Refresh(); } }
        private void PlacePopup()
        {
            var area = WorkingArea(); bubble.LimitHeight(area.Height - 16); popup.UpdateLayout(); double width = popup.ActualWidth, height = popup.ActualHeight;
            double x = Left >= area.Left + width + 16 ? Left - width - 12 : Left + Width + 12;
            popup.Left = Math.Max(area.Left + 8, Math.Min(x, area.Right - width - 8)); popup.Top = Math.Max(area.Top + 8, Math.Min(Top + Height / 2 - height / 2, area.Bottom - height - 8));
        }
        private void BringPet() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; ClampPosition(); Activate(); }
        private void HidePet() { popup.Hide(); pressed = null; ReleaseMouseCapture(); Hide(); }
        private void Exit() { Close(); }
        private void UpdateState()
        {
            bubble.SetState(result, scanning, scanError);
            pet.Caption.Text = scanning && popup.IsVisible ? "正在读取设备电量…" : result == null ? "点击小人 · 查看状态" : result.Devices.Count + " 个设备 · 点击查看";
            if (popup.IsVisible) PlacePopup();
        }
        private async void Refresh()
        {
            if (exiting || !popup.IsVisible) return;
            if (scanning) { refreshQueued = true; if (queryCancellation != null) queryCancellation.Cancel(); return; }
            scanning = true; refreshQueued = false; scanError = null;
            result = null; thermal = null; bubble.BeginQuery(); UpdateState();
            var cancellation = new CancellationTokenSource(); queryCancellation = cancellation;
            var token = cancellation.Token; int generation = popupGeneration;
            bool includeUsb = settings.IncludeUsb;
            try
            {
                var batteryTask = Task.Run(delegate { return new WindowsBatteryProvider().Scan(includeUsb, token); });
                var temperatureTask = Task.Run(delegate { return ThermalProvider.Read(token); });
                await Task.WhenAll(batteryTask, temperatureTask);
                if (!exiting && popup.IsVisible && generation == popupGeneration && !token.IsCancellationRequested)
                { result = batteryTask.Result; thermal = temperatureTask.Result; bubble.SetTemperatures(thermal); scanError = null; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLog.Write(ex.ToString()); if (!exiting && popup.IsVisible && generation == popupGeneration) scanError = "读取失败，请重试。详见本地诊断日志。"; }
            finally
            {
                scanning = false; if (queryCancellation == cancellation) queryCancellation = null; cancellation.Dispose();
                if (!exiting) { UpdateState(); if (popup.IsVisible && (refreshQueued || includeUsb != settings.IncludeUsb)) Refresh(); }
            }
        }
        private void Export()
        {
            var dialog = new SaveFileDialog { FileName = "设备电量诊断.json", Filter = "JSON 文件|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { var diagnostic = result ?? new ScanResult(); diagnostic.Temperatures = thermal; JsonFile.Write(dialog.FileName, diagnostic); MessageBox.Show("已保存到：\n" + dialog.FileName, "小电量管家"); }
            catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message, "小电量管家"); }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppLog.TraceEnabled = args.Contains("--trace");
            try
            {
                if (args.Length > 0 && args[0] == "--self-test") return Verification.Run(args.Length > 1 ? args[1] : "self-test.json");
                if (args.Length > 0 && args[0] == "--temperatures") { JsonFile.Write(args.Length > 1 ? args[1] : "temperatures.json", ThermalProvider.Read()); return 0; }
                if (args.Length > 0 && args[0] == "--hid-inventory") { JsonFile.Write(args.Length > 1 ? args[1] : "hid-inventory.json", HidDescriptor.Inventory()); return 0; }
                if (args.Length > 0 && args[0] == "--skn-info") { JsonFile.Write(args.Length > 1 ? args[1] : "skn-info.json", SknReceiverInfo.Read()); return 0; }
                if (args.Length > 0 && args[0] == "--gsp370-direct")
                {
                    var nodes = Native.Nodes();
                    var map = nodes.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
                    var physical = nodes.FirstOrDefault(d => d.Id.StartsWith("USB\\VID_1395&PID_00AB\\", StringComparison.OrdinalIgnoreCase));
                    var reading = EposGsp370Battery.ReadDirect(physical, nodes, map);
                    JsonFile.Write(args.Length > 1 ? args[1] : "gsp370.json", reading);
                    return reading.Percent.HasValue || reading.Connected == false ? 0 : 1;
                }
                if (args.Length > 0 && args[0] == "--pnp-inventory") { JsonFile.Write(args.Length > 1 ? args[1] : "pnp-inventory.json", Native.Nodes()); return 0; }
                if (args.Length > 0 && (args[0] == "--render-preview" || args[0] == "--render-live")) { Verification.Preview(args.Length > 1 ? args[1] : "preview.png", args[0] == "--render-live" ? new WindowsBatteryProvider().Scan(true) : null); return 0; }
                if (args.Length > 0 && args[0] == "--diagnose")
                {
                    var path = args.Length > 1 ? args[1] : "devices.json";
                    try { var diagnostic = new WindowsBatteryProvider().Scan(true); diagnostic.Temperatures = ThermalProvider.Read(); JsonFile.Write(path, diagnostic); return 0; }
                    catch (Exception ex) { var result = new ScanResult(); result.Warnings.Add(ex.ToString()); JsonFile.Write(path, result); return 1; }
                }
                bool created;
                using (var single = new Mutex(true, "Local\\BatteryPet.DesktopWidget", out created))
                {
                    if (!created) { PetWindow.PostMessage(PetWindow.FindWindow(null, "小电量管家"), args.Contains("--show-devices") ? PetWindow.ShowMessage : PetWindow.RestoreMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { AppLog.Write(e.Exception.ToString()); MessageBox.Show("遇到错误，详细信息已写入本地日志。\n" + e.Exception.Message, "小电量管家"); e.Handled = true; };
                    app.Run(new PetWindow(args.Contains("--show-devices")));
                }
                return 0;
            }
            catch (Exception ex) { AppLog.Write(ex.ToString()); return 1; }
        }
    }
}
