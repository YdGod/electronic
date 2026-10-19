using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BatteryPet
{
    internal static class Theme
    {
        internal static readonly Brush Text = Brush("#F4F5FC"), Muted = Brush("#9098B3"), Accent = Brush("#B8ADFF"), Green = Brush("#70E3B3");
        internal static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI, Segoe UI");
        internal static Brush Brush(string color) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush; }
        internal static TextBlock Label(string text, double size, Brush color)
        { return new TextBlock { Text = text, FontFamily = Font, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center }; }
        internal static BitmapImage Pet()
        {
            return PetSkins.Find(PetSkins.DefaultId).Frame(false);
        }
        internal static Button Button(string text, bool primary)
        {
            var button = new Button { Content = text, FontFamily = Font, FontSize = 12, Height = 34, Foreground = primary ? Brush("#19182D") : Text, Cursor = Cursors.Hand, BorderThickness = new Thickness(0), Padding = new Thickness(12, 0, 12, 0) };
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); border.SetValue(Border.BackgroundProperty, primary ? Accent : Brush("#292D44"));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter); template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82)); template.Triggers.Add(hover);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5)); template.Triggers.Add(disabled);
            button.Template = template; return button;
        }
        internal static FrameworkElement DeviceIcon(string kind)
        {
            string path;
            if (kind.Contains("鼠标")) path = "M 12,2 C 6,2 4,6 4,11 L 4,19 C 4,25 8,28 12,28 C 16,28 20,25 20,19 L 20,11 C 20,6 18,2 12,2 M 12,3 L 12,13 M 11.9,7 L 11.9,10";
            else if (kind.Contains("键盘")) path = "M 2,7 L 27,7 Q 29,7 29,9 L 29,22 Q 29,24 27,24 L 2,24 Q 0,24 0,22 L 0,9 Q 0,7 2,7 M 5,12 L 6,12 M 11,12 L 12,12 M 17,12 L 18,12 M 23,12 L 24,12 M 5,17 L 6,17 M 11,17 L 12,17 M 17,17 L 18,17 M 23,17 L 24,17 M 8,21 L 21,21";
            else if (kind.Contains("耳机")) path = "M 3,17 L 3,13 C 3,0 27,0 27,13 L 27,17 M 3,14 L 7,14 Q 9,14 9,16 L 9,24 Q 9,26 7,26 L 3,26 Q 1,26 1,24 L 1,16 Q 1,14 3,14 M 23,14 L 27,14 Q 29,14 29,16 L 29,24 Q 29,26 27,26 L 23,26 Q 21,26 21,24 L 21,16 Q 21,14 23,14";
            else path = "M 13,1 L 13,27 L 23,19 L 5,7 L 13,1 L 23,9 L 5,21";
            var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Stroke = Accent, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(13), Background = Brush("#30304B"), Child = icon };
        }
    }

    internal sealed class BatteryGlyph : FrameworkElement
    {
        private readonly int? percent;
        private readonly bool charging;
        internal BatteryGlyph(int? value, bool? charge) { percent = value; charging = charge == true; Width = 76; Height = 34; AutomationProperties.SetName(this, value.HasValue ? value.Value + "%" : "暂未读取电量"); }
        protected override AutomationPeer OnCreateAutomationPeer() { return new FrameworkElementAutomationPeer(this); }
        protected override void OnRender(DrawingContext dc)
        {
            var color = charging ? Theme.Green : Brushes.White;
            dc.DrawRoundedRectangle(null, new Pen(color, 1.6), new Rect(1, 1, 68, 30), 6, 6);
            dc.DrawRoundedRectangle(color, null, new Rect(70, 10, 4, 12), 1.5, 1.5);
            if (percent.HasValue && percent.Value > 0)
            {
                var fill = color.Clone(); fill.Opacity = 0.19;
                dc.DrawRoundedRectangle(fill, null, new Rect(5, 5, 60 * percent.Value / 100.0, 22), 3, 3);
            }
            var source = PresentationSource.FromVisual(this);
            double pixelsPerDip = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformToDevice.M11 : 1;
            var text = new FormattedText(percent.HasValue ? percent.Value + "%" : "—", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 15, color, pixelsPerDip);
            dc.DrawText(text, new Point(35 - text.Width / 2, 16 - text.Height / 2));
        }
    }

    internal sealed class BubbleView : Border
    {
        private readonly StackPanel list;
        private readonly Grid temperatures;
        private readonly ScrollViewer contentScroll;
        private readonly TextBlock count, status, notice;
        private readonly Button refresh;
        private readonly Button skinButton;
        private readonly ContextMenu skinMenu;
        private readonly Dictionary<string, Button> tabs = new Dictionary<string, Button>();
        private ScanResult current;
        private string filter = "全部";
        internal Action RefreshRequested, CloseRequested, HideRequested;
        internal Action<string> SkinSelected;
        internal Action SkinMenuClosed;
        internal bool IsSkinMenuOpen { get { return skinMenu.IsOpen; } }

        internal BubbleView()
        {
            Width = 380; CornerRadius = new CornerRadius(24); Background = Theme.Brush("#151928"); BorderBrush = Theme.Brush("#42445E"); BorderThickness = new Thickness(1); Padding = new Thickness(22, 20, 22, 18);
            var stack = new StackPanel();
            contentScroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height - 80) }; Child = contentScroll;
            var eyebrow = Theme.Label("BATTERY COMPANION", 9, Theme.Accent); eyebrow.FontWeight = FontWeights.SemiBold; eyebrow.Margin = new Thickness(0, 0, 0, 7); stack.Children.Add(eyebrow);
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Theme.Label("电量与温度", 23, Theme.Text); title.FontWeight = FontWeights.SemiBold; header.Children.Add(title);
            skinButton = Theme.Button("切换皮肤", false); skinButton.MinWidth = 76; skinButton.Height = 30; skinButton.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetName(skinButton, "切换小人皮肤"); AutomationProperties.SetAutomationId(skinButton, "SwitchPetSkinButton");
            skinMenu = SkinPicker.Create(skinButton);
            foreach (var available in PetSkins.All)
            {
                var selected = available;
                var item = SkinPicker.Item(selected);
                item.Click += delegate { if (SkinSelected != null) SkinSelected(selected.Id); else SetSkin(selected.Id); };
                skinMenu.Items.Add(item);
            }
            skinMenu.Closed += delegate { if (SkinMenuClosed != null) SkinMenuClosed(); };
            skinButton.ContextMenu = skinMenu;
            skinButton.Click += delegate { skinMenu.IsOpen = !skinMenu.IsOpen; };
            SetSkin(PetSkins.DefaultId);
            Grid.SetColumn(skinButton, 1); header.Children.Add(skinButton);
            var close = Theme.Button("×", false); close.Width = 30; close.Height = 30; close.FontSize = 20; close.ToolTip = "收起"; close.Click += delegate { if (CloseRequested != null) CloseRequested(); }; Grid.SetColumn(close, 2); header.Children.Add(close); stack.Children.Add(header);
            var subtitle = Theme.Label("展开时即时查询 · 收起后停止采集", 11, Theme.Muted); subtitle.Margin = new Thickness(0, 7, 0, 16); stack.Children.Add(subtitle);
            temperatures = new Grid { Margin = new Thickness(0, 0, 0, 17) };
            temperatures.ColumnDefinitions.Add(new ColumnDefinition()); temperatures.ColumnDefinitions.Add(new ColumnDefinition()); stack.Children.Add(temperatures);
            SetTemperatures(null);
            var info = new DockPanel(); count = Theme.Label("正在发现设备…", 11, Theme.Text); info.Children.Add(count);
            var local = Theme.Label("●  本机读取", 10, Theme.Green); local.HorizontalAlignment = HorizontalAlignment.Right; info.Children.Add(local); info.Margin = new Thickness(0, 0, 0, 13); stack.Children.Add(info);
            var tabPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var value in new[] { "全部", "鼠标", "键盘", "耳机" })
            {
                var name = value; var tab = Theme.Button(name, false); tab.Width = 76; tab.Height = 28; tab.Margin = new Thickness(0, 0, 8, 0); tab.Foreground = name == filter ? Theme.Accent : Theme.Muted;
                tab.Click += delegate { filter = name; foreach (var pair in tabs) pair.Value.Foreground = pair.Key == filter ? Theme.Accent : Theme.Muted; Render(); }; tabs[name] = tab; tabPanel.Children.Add(tab);
            }
            stack.Children.Add(tabPanel);
            list = new StackPanel(); var scroll = new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; stack.Children.Add(scroll);
            notice = Theme.Label("", 10, Theme.Muted); notice.TextWrapping = TextWrapping.Wrap; notice.LineHeight = 17; notice.Margin = new Thickness(0, 9, 0, 12); stack.Children.Add(notice);
            var footer = new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            status = Theme.Label("展开时查询一次", 10, Theme.Muted); status.TextTrimming = TextTrimming.CharacterEllipsis; status.Margin = new Thickness(0, 0, 8, 0); footer.Children.Add(status);
            var hide = Theme.Button("隐藏小人", false); hide.MinWidth = 84; hide.Margin = new Thickness(0, 0, 8, 0);
            hide.ToolTip = "隐藏小人和对话框 · 双击托盘图标恢复";
            hide.Click += delegate { if (HideRequested != null) HideRequested(); }; Grid.SetColumn(hide, 1); footer.Children.Add(hide);
            AutomationProperties.SetAutomationId(hide, "HidePetFromDialogButton");
            refresh = Theme.Button("↻  刷新设备", true); refresh.MinWidth = 112; refresh.Click += delegate { if (RefreshRequested != null) RefreshRequested(); }; Grid.SetColumn(refresh, 2); footer.Children.Add(refresh); stack.Children.Add(footer);
            AutomationProperties.SetAutomationId(refresh, "RefreshStatusButton");
            AutomationProperties.SetAutomationId(status, "QueryStatus");
            var hint = Theme.Label("拖动小人移动位置 · 右键小人打开设置", 9, Theme.Muted); hint.HorizontalAlignment = HorizontalAlignment.Center; hint.Margin = new Thickness(0, 13, 0, 0); stack.Children.Add(hint);
            Render();
        }
        internal void SetSkin(string id)
        {
            var skin = PetSkins.Find(id);
            foreach (MenuItem item in skinMenu.Items) item.IsChecked = (string)item.Tag == skin.Id;
            skinButton.ToolTip = "切换小人皮肤 · 当前：" + skin.Name;
            AutomationProperties.SetHelpText(skinButton, "当前皮肤：" + skin.Name);
        }
        internal void CloseSkinMenu() { skinMenu.IsOpen = false; }
        internal void LimitHeight(double height) { contentScroll.MaxHeight = Math.Max(120, height - 42); }
        internal void BeginQuery() { current = null; SetTemperatures(null); }
        internal void SetTemperatures(ThermalSnapshot snapshot)
        {
            temperatures.Children.Clear(); temperatures.RowDefinitions.Clear();
            var readings = snapshot == null ? new List<TemperatureReading> { new TemperatureReading { Kind = "CPU", Name = "CPU", Note = "等待读取温度" }, new TemperatureReading { Kind = "GPU", Name = "显卡", Note = "等待读取温度" } } : snapshot.Readings;
            int gpuIndex = 0;
            for (int i = 0; i < readings.Count; i++)
            {
                var reading = readings[i];
                if (i % 2 == 0) temperatures.RowDefinitions.Add(new RowDefinition());
                var card = new Border { Background = Theme.Brush("#222638"), BorderBrush = Theme.Brush("#363B52"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(i % 2 == 0 ? 0 : 5, i < 2 ? 0 : 8, i % 2 == 0 ? 5 : 0, 0) };
                var content = new StackPanel(); card.Child = content;
                content.Children.Add(Theme.Label(reading.Kind == "CPU" ? "CPU 温度" : "显卡温度", 10, Theme.Accent));
                var number = Theme.Label(reading.DisplayValue, 25, Theme.Text); number.FontWeight = FontWeights.SemiBold; number.Margin = new Thickness(0, 3, 0, 1);
                AutomationProperties.SetAutomationId(number, reading.Kind == "CPU" ? "Temperature_CPU" : "Temperature_GPU_" + gpuIndex++);
                AutomationProperties.SetHelpText(number, reading.Sensor + " | " + reading.Source + " | " + (reading.ReadAt.HasValue ? reading.ReadAt.Value.ToUniversalTime().ToString("o") : "unknown"));
                content.Children.Add(number);
                var model = Theme.Label(reading.Name, 9, Theme.Muted); model.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(model);
                var label = Theme.Label(reading.Celsius.HasValue ? reading.Note : "温度未知", 9, Theme.Muted); label.Margin = new Thickness(0, 3, 0, 0); content.Children.Add(label);
                card.ToolTip = reading.Name + "\n" + reading.Sensor + " · " + reading.Source + "\n" + reading.Note + (reading.ReadAt.HasValue ? "\n读取于 " + reading.ReadAt.Value.ToLocalTime().ToString("HH:mm:ss") : "");
                Grid.SetColumn(card, i % 2); Grid.SetRow(card, i / 2); temperatures.Children.Add(card);
            }
        }
        internal void SetState(ScanResult result, bool scanning, string error)
        {
            if (result != null) current = result;
            refresh.IsEnabled = !scanning;
            count.Text = scanning ? "正在读取设备…" : current == null ? "设备列表" : "已发现 " + current.Devices.Count + " 个设备";
            status.Text = scanning ? "读取中，请稍候" : error ?? (current == null ? "等待首次读取" : "电量更新 " + current.ScannedAt.ToString("HH:mm:ss"));
            Render();
            if (!String.IsNullOrEmpty(error)) { notice.Text = error; notice.Foreground = Theme.Brush("#EAC68F"); }
        }
        private void Render()
        {
            list.Children.Clear();
            var devices = current == null ? new List<DeviceBattery>() : current.Devices.Where(d => filter == "全部" || d.Kind.Contains(filter)).ToList();
            if (devices.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(12, 25, 12, 25) };
                var symbol = Theme.Label("✧", 34, Theme.Accent); symbol.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(symbol);
                var message = Theme.Label(current == null ? "正在寻找你的设备" : "还没有发现" + (filter == "全部" ? "可显示的设备" : filter), 13, Theme.Text); message.HorizontalAlignment = HorizontalAlignment.Center; message.Margin = new Thickness(0, 10, 0, 0); empty.Children.Add(message);
                var detail = Theme.Label("连接蓝牙设备或插入 USB 接收器后刷新", 10, Theme.Muted); detail.TextWrapping = TextWrapping.Wrap; detail.TextAlignment = TextAlignment.Center; detail.Margin = new Thickness(0, 8, 0, 0); empty.Children.Add(detail); list.Children.Add(empty);
            }
            foreach (var device in devices) list.Children.Add(Row(device));
            notice.Foreground = Theme.Muted;
            notice.Text = "白色：普通电量 · 绿色：已确认正在充电\n暂未读取的电量显示为 —";
        }
        private static Border Row(DeviceBattery device)
        {
            var row = new Border { Background = Theme.Brush("#222638"), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), BorderBrush = Theme.Brush("#2D3247"), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 9) };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) }); row.Child = grid;
            grid.Children.Add(Theme.DeviceIcon(device.Kind));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) }; Grid.SetColumn(text, 1); grid.Children.Add(text);
            var kind = Theme.Label(device.Kind + " · " + device.Connection, 9, Theme.Muted); kind.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(kind);
            var name = Theme.Label(device.Name, 12, Theme.Text); name.FontWeight = FontWeights.SemiBold; name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(0, 3, 0, 4); text.Children.Add(name);
            var statusLabel = Theme.Label(device.StatusText, 9, device.Charging == true ? Theme.Green : Theme.Muted); text.Children.Add(statusLabel);
            var battery = new BatteryGlyph(device.Percent, device.Charging) { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(battery, 2); grid.Children.Add(battery);
            AutomationProperties.SetAutomationId(battery, "Battery_" + device.Name);
            AutomationProperties.SetName(battery, device.Name + "：" + (device.Percent.HasValue ? device.Percent.Value + "%" : "—") + "，" + device.StatusText);
            AutomationProperties.SetHelpText(battery, device.Source + " | " + (device.ReadAt.HasValue ? device.ReadAt.Value.ToUniversalTime().ToString("o") : "unknown"));
            row.ToolTip = device.Name + "\n" + device.Kind + " · " + device.Connection + "\n" + device.Source + "\n" + device.Note;
            return row;
        }
    }

    internal sealed class PetView : Grid
    {
        internal Action HideRequested;
        internal readonly TextBlock Caption;
        private readonly Grid sprite;
        private readonly Image previous, current;
        private BitmapSource target;
        private PetSkin skin;
        private bool popupVisible;
        private int transition;
        internal string SkinId { get { return skin.Id; } }
        internal bool IsSaluting { get { return skin.Id == "phoebe" && popupVisible; } }
        internal PetView(string skinId = PetSkins.DefaultId)
        {
            Width = 210; Height = 236;
            sprite = new Grid { Width = 210, Height = 210, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand };
            previous = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
            current = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(previous, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(current, BitmapScalingMode.HighQuality);
            sprite.Children.Add(previous); sprite.Children.Add(current); Children.Add(sprite);
            SetSkin(skinId, false);
            Unloaded += delegate { FinishTransition(); };
            var hide = Theme.Button("−", false); hide.Width = 28; hide.Height = 25; hide.FontSize = 17;
            hide.HorizontalAlignment = HorizontalAlignment.Right; hide.VerticalAlignment = VerticalAlignment.Top; hide.Margin = new Thickness(0, 5, 5, 0);
            hide.ToolTip = "隐藏小人 · 双击任务栏托盘图标恢复";
            AutomationProperties.SetName(hide, "隐藏小人"); AutomationProperties.SetAutomationId(hide, "HidePetButton");
            hide.Click += delegate { if (HideRequested != null) HideRequested(); }; Children.Add(hide);
            Caption = Theme.Label("点击小人 · 查看状态", 10, Theme.Text); Caption.HorizontalAlignment = HorizontalAlignment.Center;
            var pill = new Border { Background = Theme.Brush("#E51B1E31"), BorderBrush = Theme.Brush("#66565C7C"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(13, 6, 13, 6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Child = Caption }; Children.Add(pill);
        }
        internal void SetSkin(string id, bool animate = true)
        {
            skin = PetSkins.Find(id);
            AutomationProperties.SetName(sprite, skin.Name);
            ShowFrame(animate);
        }
        internal void SetPopupVisible(bool visible, bool animate = true)
        {
            popupVisible = visible;
            ShowFrame(animate);
        }
        private void FinishTransition()
        {
            transition++;
            current.BeginAnimation(UIElement.OpacityProperty, null);
            current.Source = target; current.Opacity = 1; previous.Source = null;
        }
        private void ShowFrame(bool animate)
        {
            var next = skin.Frame(popupVisible);
            if (ReferenceEquals(next, target)) return;
            // Snapshot just the sprite when interrupting a fade. Repeated clicks
            // continue from the currently displayed pose without a visual jump.
            BitmapSource from = target;
            if (animate && IsLoaded && sprite.ActualWidth > 0 && current.Opacity < 1)
            {
                var snapshot = new RenderTargetBitmap(610, 610, 610.0 / 210 * 96, 610.0 / 210 * 96, PixelFormats.Pbgra32);
                snapshot.Render(sprite); snapshot.Freeze(); from = snapshot;
            }
            target = next;
            FinishTransition();
            if (!animate || !IsLoaded || !SystemParameters.ClientAreaAnimation || from == null) return;
            previous.Source = from; current.Opacity = 0;
            int generation = transition;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            fade.Completed += delegate { if (generation == transition) FinishTransition(); };
            current.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
