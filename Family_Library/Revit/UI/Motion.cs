using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Family_Library.UI
{
    /// <summary>
    /// Minimal motion for panel changes only (never for list rows), as in RK Tools Sentinel:
    /// <c>Motion.Reveal="True"</c> fades an element in and settles it from a small offset (<c>Motion.FromY</c>) each
    /// time it becomes visible. Animations start from the current value (SnapshotAndReplace), so a reveal can be
    /// interrupted without a jump. With Windows animations off it is a 100 ms opacity fade with no movement.
    /// WPF has no springs; a short ease-out stands in for a critically damped spring.
    /// </summary>
    public static class Motion
    {
        public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached(
            "Reveal", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnRevealChanged));

        public static readonly DependencyProperty FromYProperty = DependencyProperty.RegisterAttached(
            "FromY", typeof(double), typeof(Motion), new PropertyMetadata(6.0));

        public static bool GetReveal(DependencyObject d) => (bool)d.GetValue(RevealProperty);
        public static void SetReveal(DependencyObject d, bool value) => d.SetValue(RevealProperty, value);
        public static double GetFromY(DependencyObject d) => (double)d.GetValue(FromYProperty);
        public static void SetFromY(DependencyObject d, double value) => d.SetValue(FromYProperty, value);

        private static void OnRevealChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement el)) return;
            el.IsVisibleChanged -= OnVisibleChanged;
            if ((bool)e.NewValue) el.IsVisibleChanged += OnVisibleChanged;
        }

        private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) Play((FrameworkElement)sender);
        }

        public static void Play(FrameworkElement el, double? fromY = null)
        {
            var reduced = ThemeManager.ReducedMotion;
            var duration = TimeSpan.FromMilliseconds(reduced ? 100 : 180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation { From = el.Opacity < 0.99 ? (double?)null : 0.0, To = 1.0, Duration = duration, EasingFunction = ease };
            el.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);

            if (reduced) return;
            if (!(el.RenderTransform is TranslateTransform t) || t.IsFrozen)
            {
                t = new TranslateTransform();
                el.RenderTransform = t;
            }
            // Interrupted mid-slide: continue from the current (presentation) value instead of jumping back.
            var inFlight = Math.Abs(t.Y) > 0.01;
            var slide = new DoubleAnimation { From = inFlight ? (double?)null : (fromY ?? GetFromY(el)), To = 0, Duration = duration, EasingFunction = ease };
            t.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
