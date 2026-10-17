using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BtoSync
{
	public enum DialogKind { Info, Warning, Error, Question, Danger }

	// The app's own message box: dark card, colored icon, the app's buttons. Enter = main button, Esc = cancel.
	public static class Dialog
	{
		// Returns true when the main button was pressed. No cancel button when cancel is null.
		public static bool Show(Window owner, string title, string text = null, DialogKind kind = DialogKind.Info, string ok = "OK", string cancel = null)
			=> Choose(owner, title, text, kind, ok, cancel) == 1;

		// Three answers: 1 = ok (main button), 2 = other (between the two), 0 = cancel or closed.
		public static int Choose(Window owner, string title, string text, DialogKind kind, string ok, string cancel, string other = null)
		{
			var app = Application.Current;
			Brush R(string key) => (Brush)app.FindResource(key);
			var (glyph, color) = kind switch
			{
				DialogKind.Warning => ("\uE7BA", Theme.Warn),
				DialogKind.Error => ("\uEA39", Theme.Bad),
				DialogKind.Question => ("\uE9CE", Theme.Blue),
				DialogKind.Danger => ("\uE74D", Theme.Bad),
				_ => ("\uE946", Theme.Blue),
			};
			bool owned = owner != null && owner.IsVisible;
			var w = new Window
			{
				Owner = owned ? owner : null, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
				ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = !owned,
				WindowStartupLocation = owned ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
				Title = title, FontFamily = owner?.FontFamily ?? SystemFonts.MessageFontFamily, Foreground = R("Fg"),
			};
			int result = 0;

			var icon = new Border
			{
				Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(((SolidColorBrush)color).Color) { Opacity = 0.16 },
				VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0),
				Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)app.FindResource("Icons"), FontSize = 16, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
			};
			var body = new StackPanel { MaxWidth = 470 };
			body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) });
			if (!string.IsNullOrWhiteSpace(text))
				body.Children.Add(new TextBlock { Text = text.Trim(), Foreground = R("Body"), TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 10, 0, 0) });

			var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
			Button no = null;
			if (cancel != null)
			{
				no = new Button { Content = cancel, Style = (Style)app.FindResource("Btn"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
				no.Click += (s, e) => w.Close();
				buttons.Children.Add(no);
			}
			if (other != null)
			{
				var mid = new Button { Content = other, Style = (Style)app.FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
				mid.Click += (s, e) => { result = 2; w.Close(); };
				buttons.Children.Add(mid);
			}
			// a destructive action is never the default: Enter on it would delete by mistake
			bool safe = kind == DialogKind.Danger && no != null;
			var yes = new Button { Content = ok, Style = (Style)app.FindResource(kind == DialogKind.Danger ? "Danger" : "Primary"), IsDefault = !safe, IsCancel = cancel == null, MinWidth = 90 };
			if (safe) no.IsDefault = true;
			yes.Click += (s, e) => { result = 1; w.Close(); };
			buttons.Children.Add(yes);

			var row = new DockPanel();
			DockPanel.SetDock(icon, Dock.Left);
			row.Children.Add(icon);
			row.Children.Add(body);
			var content = new StackPanel();
			content.Children.Add(row);
			content.Children.Add(buttons);
			w.Content = new Border
			{
				Background = R("Surface"), BorderBrush = R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
				Padding = new Thickness(22, 20, 22, 18), Margin = new Thickness(18), MinWidth = 380, Child = content,
				Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.45, Color = Colors.Black },
			};
			w.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) w.DragMove(); };
			w.Loaded += (s, e) => (safe ? no : yes).Focus();
			w.ShowDialog();
			return result;
		}
	}
}
