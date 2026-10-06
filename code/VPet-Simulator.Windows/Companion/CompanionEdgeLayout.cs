using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

// Positions the visible character at the monitor edge and keeps its separate bubble inside the work area.
public static class CompanionEdgeLayout
{
    private sealed class Follow
    {
        private readonly Window pet;
        private readonly Border bubble;
        private readonly System.Windows.Controls.TextBlock text;
        private readonly System.Windows.Controls.TextBlock measuringText = new();
        private readonly DependencyPropertyDescriptor textChange;
        private Window? popup;
        private bool updating;
        private bool closed;
        private bool queued;
        private readonly DependencyPropertyDescriptor visibility;
        public Follow(Window w)
        {
            pet = w;
            bubble = ((VPet_Simulator.Windows.MainWindow)w).CompanionBubble;
            text = ((VPet_Simulator.Windows.MainWindow)w).CompanionBubbleText;
            textChange = DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.TextBlock.TextProperty, typeof(System.Windows.Controls.TextBlock));
            textChange.AddValueChanged(text, Changed);
            visibility = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border));
            visibility.AddValueChanged(bubble, Changed);
            pet.Loaded += Loaded;
            pet.LocationChanged += Changed;
            pet.SizeChanged += Changed;
            pet.IsVisibleChanged += Visible;
            pet.DpiChanged += DpiChanged;
            pet.Closed += Closed;
            bubble.SizeChanged += Changed;
            if (pet.IsLoaded)
            {
                Create();
            }
        }

        private void Loaded(object s, RoutedEventArgs e)
        {
            Create();
            Update();
        }

        private void Changed(object? s, EventArgs e)
        {
            Update();
            QueueUpdate();
        }

        private void QueueUpdate()
        {
            if (!queued && !closed)
            {
                queued = true;
                pet.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
                {
                    queued = false;
                    Update();
                }));
            }
        }

        private void Visible(object s, DependencyPropertyChangedEventArgs e)
        {
            Update();
        }

        private void DpiChanged(object s, System.Windows.DpiChangedEventArgs e)
        {
            Clamp(pet);
        }

        private void Closed(object? s, EventArgs e)
        {
            closed = true;
            visibility.RemoveValueChanged(bubble, Changed);
            textChange.RemoveValueChanged(text, Changed);
            if (popup != null)
            {
                popup.Content = null;
                popup.Close();
            }
        }

        private void Create()
        {
            if (popup == null && !closed)
            {
                ((System.Windows.Controls.Panel)bubble.Parent).Children.Remove(bubble);
                bubble.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                popup = new Window
                {
                    Title = "蓝色大肥鱼 · 对话",
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ResizeMode = ResizeMode.NoResize,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Owner = pet,
                    Content = bubble
                };
                popup.SourceInitialized += (object? s, EventArgs e) =>
                {
                    nint handle = new WindowInteropHelper(popup).Handle;
                    SetWindowLongPtr(handle, -20, new IntPtr(((IntPtr)GetWindowLongPtr(handle, -20)).ToInt64() | 0x8000080));
                    HwndSource.FromHwnd(handle)?.AddHook(NoActivate);
                };
                popup.DpiChanged += (object s, System.Windows.DpiChangedEventArgs e) =>
                {
                    pet.Dispatcher.BeginInvoke(new Action(Update), DispatcherPriority.Loaded);
                };
            }
        }

        private static nint NoActivate(nint h, int msg, nint wp, nint lp, ref bool handled)
        {
            if (msg == 33)
            {
                handled = true;
                return new IntPtr(3);
            }

            return IntPtr.Zero;
        }

        public void Update()
        {
            if (updating || closed || popup == null)
            {
                return;
            }

            updating = true;
            try
            {
                if (!pet.IsVisible || bubble.Visibility != Visibility.Visible)
                {
                    if (popup.IsVisible)
                    {
                        popup.Hide();
                    }

                    return;
                }

                Rect area = WorkArea(pet);
                var styledBubble = bubble as VPet_Simulator.Windows.CompanionBubble;
                Rect petBounds = PetBounds(pet);
                double maximumWidth = Math.Max(1.0, Math.Min(300.0, area.Width - 16.0));
                bubble.MaxWidth = maximumWidth;
                measuringText.Text = ((VPet_Simulator.Windows.MainWindow)pet).CompanionBubbleLayoutText;
                measuringText.FontFamily = text.FontFamily;
                measuringText.FontSize = text.FontSize;
                measuringText.FontWeight = text.FontWeight;
                measuringText.TextWrapping = text.TextWrapping;
                measuringText.Margin = text.Margin;
                double horizontal = bubble.Padding.Left + bubble.Padding.Right + bubble.BorderThickness.Left + bubble.BorderThickness.Right;
                double vertical = bubble.Padding.Top + bubble.Padding.Bottom + bubble.BorderThickness.Top + bubble.BorderThickness.Bottom;
                measuringText.Measure(new System.Windows.Size(Math.Max(1, maximumWidth - horizontal), double.PositiveInfinity));
                double bubbleWidth = Math.Min(maximumWidth, Math.Max(26, measuringText.DesiredSize.Width) + horizontal);
                measuringText.Measure(new System.Windows.Size(Math.Max(1, bubbleWidth - horizontal), double.PositiveInfinity));
                double bubbleHeight = Math.Min(Math.Max(1.0, area.Height - 16.0), Math.Max(22, measuringText.DesiredSize.Height) + vertical);
                double left = Limit(pet.Left + petBounds.Left + petBounds.Width / 2.0 - bubbleWidth / 2.0, area.Left + 8.0, area.Right - bubbleWidth - 8.0);
                double top = pet.Top + petBounds.Top - bubbleHeight - 10.0;
                bool belowPet = false;
                if (top < area.Top + 8.0)
                {
                    belowPet = true;
                    top = pet.Top + petBounds.Bottom + 10.0;
                }

                top = Limit(top, area.Top + 8.0, area.Bottom - bubbleHeight - 8.0);
                styledBubble?.PointAt(pet.Left + petBounds.Left + petBounds.Width / 2.0 - left, belowPet);
                popup.Width = Math.Max(1.0, bubbleWidth);
                popup.Height = Math.Max(1.0, bubbleHeight);
                popup.Left = left;
                popup.Top = top;
                if (!popup.IsVisible)
                {
                    popup.Show();
                    QueueUpdate();
                }
            }
            finally
            {
                updating = false;
            }
        }
    }

    private static readonly ConditionalWeakTable<Window, Follow> followers = new ConditionalWeakTable<Window, Follow>();
    public static double SpriteSize(Window w)
    {
        return ((VPet_Simulator.Windows.MainWindow)w).CompanionSpriteSize;
    }

    public static Rect PetBounds(Window w)
    {
        return ((VPet_Simulator.Windows.MainWindow)w).CompanionVisibleBounds;
    }

    public static Rect WorkArea(Window w)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(w);
        Rect petBounds = PetBounds(w);
        if (PresentationSource.FromVisual(w) != null)
        {
            System.Windows.Point screenCenter = w.PointToScreen(new System.Windows.Point(petBounds.Left + petBounds.Width / 2.0, petBounds.Top + petBounds.Height / 2.0));
            Rectangle workingArea = Screen.FromPoint(new System.Drawing.Point((int)screenCenter.X, (int)screenCenter.Y)).WorkingArea;
            System.Windows.Point localTopLeft = w.PointFromScreen(new System.Windows.Point(workingArea.Left, workingArea.Top));
            System.Windows.Point localBottomRight = w.PointFromScreen(new System.Windows.Point(workingArea.Right, workingArea.Bottom));
            return new Rect(w.Left + localTopLeft.X, w.Top + localTopLeft.Y, localBottomRight.X - localTopLeft.X, localBottomRight.Y - localTopLeft.Y);
        }

        Rectangle primaryArea = (Screen.PrimaryScreen ?? throw new InvalidOperationException("无法找到主显示器")).WorkingArea;
        return new Rect((double)primaryArea.Left / dpi.DpiScaleX, (double)primaryArea.Top / dpi.DpiScaleY, (double)primaryArea.Width / dpi.DpiScaleX, (double)primaryArea.Height / dpi.DpiScaleY);
    }

    private static double Limit(double v, double min, double max)
    {
        return Math.Clamp(double.IsFinite(v) ? v : min, min, Math.Max(min, max));
    }

    public static void Clamp(Window w)
    {
        Follow follower = followers.GetValue(w, (Window x) => new Follow(x));
        Rect area = WorkArea(w);
        Rect petBounds = PetBounds(w);
        double left = Limit(w.Left, area.Left - petBounds.Left, area.Right - petBounds.Right);
        double top = Limit(w.Top, area.Top - petBounds.Top, area.Bottom - petBounds.Bottom);
        if (Math.Abs(left + petBounds.Left - area.Left) < 14.0)
        {
            left = area.Left - petBounds.Left;
        }

        if (Math.Abs(left + petBounds.Right - area.Right) < 14.0)
        {
            left = area.Right - petBounds.Right;
        }

        if (Math.Abs(top + petBounds.Top - area.Top) < 14.0)
        {
            top = area.Top - petBounds.Top;
        }

        if (Math.Abs(top + petBounds.Bottom - area.Bottom) < 14.0)
        {
            top = area.Bottom - petBounds.Bottom;
        }

        w.Left = left;
        w.Top = top;
        follower.Update();
    }

    public static void Refresh(Window w) => followers.GetValue(w, x => new Follow(x)).Update();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
