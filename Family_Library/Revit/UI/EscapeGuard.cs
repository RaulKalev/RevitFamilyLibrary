using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Family_Library.UI
{
    /// <summary>
    /// Keeps Escape inside a modeless window (as in CAD Manager). Revit treats Escape in a modeless window as
    /// "cancel command" and minimizes / deactivates the window, so the key is consumed before Revit sees it
    /// (thread pre-process + window hook + PreviewKeyDown fallback) and handed to <c>onEscape</c> instead.
    /// </summary>
    public sealed class EscapeGuard
    {
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int EscapeVirtualKey = 0x1B;

        private readonly Window _window;
        private readonly Action _onEscape;
        private HwndSource _source;

        private EscapeGuard(Window window, Action onEscape)
        {
            _window = window;
            _onEscape = onEscape;
            window.SourceInitialized += (s, e) =>
            {
                _source = PresentationSource.FromVisual(window) as HwndSource;
                _source?.AddHook(Hook);
                ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
            };
            window.Closed += (s, e) =>
            {
                _source?.RemoveHook(Hook);
                _source = null;
                ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
            };
            window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                _onEscape();
                e.Handled = true;
            };
        }

        public static EscapeGuard Attach(Window window, Action onEscape) => new EscapeGuard(window, onEscape);

        private void OnThreadPreprocessMessage(ref MSG message, ref bool handled)
        {
            if (handled || !_window.IsActive || message.wParam.ToInt64() != EscapeVirtualKey)
                return;

            var isKeyMessage = message.message == WmKeyDown || message.message == WmKeyUp ||
                               message.message == WmSysKeyDown || message.message == WmSysKeyUp;
            if (!isKeyMessage)
                return;

            if (message.message == WmKeyDown || message.message == WmSysKeyDown)
                _onEscape();

            // Consume both key-down and key-up so Revit never sees Escape.
            handled = true;
        }

        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((message == WmKeyDown || message == WmSysKeyDown) && wParam.ToInt64() == EscapeVirtualKey)
            {
                _onEscape();
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
