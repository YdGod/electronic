using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BatteryPet
{
    internal static class SkinPicker
    {
        internal static ContextMenu Create(Button anchor)
        {
            var menu = new ContextMenu {
                PlacementTarget = anchor, Placement = PlacementMode.Bottom,
                Width = 288, HorizontalOffset = -202, VerticalOffset = 2,
                Background = Brushes.Transparent, Foreground = Theme.Text,
                FontFamily = Theme.Font, HasDropShadow = false,
                OverridesDefaultStyle = true, SnapsToDevicePixels = true, UseLayoutRounding = true
            };
            menu.Opened += delegate { menu.HorizontalOffset = anchor.ActualWidth - menu.Width + 10; };
            var template = new ControlTemplate(typeof(ContextMenu));
            // Leave transparent space around the surface for its soft shadow.
            var surface = new FrameworkElementFactory(typeof(Border));
            surface.SetValue(FrameworkElement.MarginProperty, new Thickness(10));
            surface.SetValue(Border.BackgroundProperty, Theme.Brush("#1B2032"));
            surface.SetValue(Border.BorderBrushProperty, Theme.Brush("#454B67"));
            surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
            surface.SetValue(Border.PaddingProperty, new Thickness(8));
            surface.SetValue(UIElement.EffectProperty, new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4, Opacity = 0.35, RenderingBias = RenderingBias.Performance });
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var heading = new FrameworkElementFactory(typeof(TextBlock));
            heading.SetValue(TextBlock.TextProperty, "选择小人皮肤");
            heading.SetValue(TextBlock.FontSizeProperty, 10.0);
            heading.SetValue(TextBlock.ForegroundProperty, Theme.Muted);
            heading.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 5, 10, 8));
            stack.AppendChild(heading);
            stack.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            surface.AppendChild(stack); template.VisualTree = surface; menu.Template = template;
            return menu;
        }

        internal static MenuItem Item(PetSkin skin)
        {
            var row = new DockPanel { LastChildFill = true };
            var image = new Image { Source = skin.Frame(false), Width = 44, Height = 44, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var portrait = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(11), Background = Theme.Brush(skin.Id == "phoebe" ? "#3D3746" : "#2D3858"), Child = image, Clip = new RectangleGeometry(new Rect(0, 0, 44, 44), 11, 11) };
            DockPanel.SetDock(portrait, Dock.Left); row.Children.Add(portrait);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var name = Theme.Label(skin.Name, 13, Theme.Text); name.FontWeight = FontWeights.SemiBold; words.Children.Add(name);
            var detail = Theme.Label(skin.Description, 10, Theme.Muted);
            detail.Margin = new Thickness(0, 4, 0, 0); words.Children.Add(detail); row.Children.Add(words);
            var item = new MenuItem {
                Header = row, Tag = skin.Id, IsCheckable = true, Cursor = Cursors.Hand,
                OverridesDefaultStyle = true, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 2), FocusVisualStyle = null
            };
            AutomationProperties.SetName(item, skin.Name);
            item.ToolTip = skin.Author + " · " + skin.License;
            AutomationProperties.SetHelpText(item, detail.Text + " · " + skin.Author);
            var template = new ControlTemplate(typeof(MenuItem));
            var border = new FrameworkElementFactory(typeof(Border), "Card");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            border.SetValue(Border.PaddingProperty, new Thickness(10));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var content = new FrameworkElementFactory(typeof(DockPanel));
            content.SetValue(DockPanel.LastChildFillProperty, true);
            var selected = new FrameworkElementFactory(typeof(Border), "Selected");
            selected.SetValue(DockPanel.DockProperty, Dock.Right);
            selected.SetValue(FrameworkElement.WidthProperty, 22.0);
            selected.SetValue(FrameworkElement.HeightProperty, 22.0);
            selected.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
            selected.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            selected.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            selected.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            selected.SetValue(Border.BackgroundProperty, Theme.Accent);
            var tick = new FrameworkElementFactory(typeof(TextBlock));
            tick.SetValue(TextBlock.TextProperty, "✓"); tick.SetValue(TextBlock.FontSizeProperty, 13.0);
            tick.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            tick.SetValue(TextBlock.ForegroundProperty, Theme.Brush("#202137"));
            tick.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            tick.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            selected.AppendChild(tick); content.AppendChild(selected);
            var header = new FrameworkElementFactory(typeof(ContentPresenter));
            header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.AppendChild(header); border.AppendChild(content); template.VisualTree = border;
            var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Brush("#29314A"), "Card"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.Brush("#4B5878"), "Card"));
            template.Triggers.Add(hover);
            var active = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            active.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Brush("#30314C"), "Card"));
            active.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.Brush("#77709D"), "Card"));
            active.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Selected"));
            template.Triggers.Add(active);
            var activeHover = new MultiTrigger();
            activeHover.Conditions.Add(new System.Windows.Condition(MenuItem.IsCheckedProperty, true));
            activeHover.Conditions.Add(new System.Windows.Condition(MenuItem.IsHighlightedProperty, true));
            activeHover.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Brush("#3A3B59"), "Card"));
            activeHover.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.Accent, "Card"));
            template.Triggers.Add(activeHover);
            item.Template = template;
            return item;
        }
    }
}
