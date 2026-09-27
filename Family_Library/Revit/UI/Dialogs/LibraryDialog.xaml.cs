using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Family_Library.UI.Dialogs
{
    public enum DialogKind { Info, Success, Question, Error }

    /// <summary>A choice shown as a command link (title + one line of explanation).</summary>
    public sealed class DialogChoice
    {
        public DialogChoice(string title, string description)
        {
            Title = title;
            Description = description;
        }

        public string Title { get; }
        public string Description { get; }
    }

    /// <summary>
    /// Themed modal dialog used instead of Revit TaskDialogs. Shown with <see cref="LibraryDialogs"/>; it is modal like
    /// TaskDialog, so it can be used inside a Revit API call (for example mid-transaction while loading families).
    /// </summary>
    public partial class LibraryDialog : Window
    {
        /// <summary>Index of the chosen command link, or -1.</summary>
        public int ChoiceIndex { get; private set; } = -1;

        /// <summary>True when the primary button (or a choice) was used; false for cancel/close.</summary>
        public bool Confirmed { get; private set; }

        public LibraryDialog()
        {
            InitializeComponent();
            new ThemeManager(this, ThemeManager.RevitIsDark()).ApplyTheme();
            Loaded += (s, e) =>
            {
                Motion.Play(Card, 8);
                // Focus the first choice when there are choices, else the primary button.
                if (ChoicePanel.Children.Count > 0) ((UIElement)ChoicePanel.Children[0]).Focus();
                else PrimaryButton.Focus();
            };
        }

        internal void Setup(DialogKind kind, string title, string message, string details,
                            IList<DialogChoice> choices, string primaryText, string secondaryText)
        {
            // Icon kinds are resolved by name: the MaterialDesign version loaded in Revit may differ from ours.
            string glyph;
            string brush;
            switch (kind)
            {
                case DialogKind.Success: glyph = "CheckCircleOutline"; brush = "Status.Ok"; break;
                case DialogKind.Question: glyph = "HelpCircleOutline"; brush = "Accent.Text"; break;
                case DialogKind.Error: glyph = "AlertCircleOutline"; brush = "Status.Error"; break;
                default: glyph = "InformationOutline"; brush = "Accent.Text"; break;
            }
            Glyph.Kind = (PackIconKind)Enum.Parse(typeof(PackIconKind), glyph);
            Glyph.SetResourceReference(ForegroundProperty, brush);

            TitleText.Text = title ?? "";
            MessageText.Text = message ?? "";
            MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

            if (!string.IsNullOrWhiteSpace(details))
            {
                DetailsText.Text = details;
                DetailsCard.Visibility = Visibility.Visible;
            }

            if (choices != null && choices.Count > 0)
            {
                ChoicePanel.Visibility = Visibility.Visible;
                for (int i = 0; i < choices.Count; i++)
                {
                    var index = i;
                    var content = new StackPanel();
                    content.Children.Add(new TextBlock
                    {
                        Text = choices[i].Title,
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    });
                    if (!string.IsNullOrWhiteSpace(choices[i].Description))
                    {
                        var desc = new TextBlock { Text = choices[i].Description, Margin = new Thickness(0, 2, 0, 0) };
                        desc.SetResourceReference(StyleProperty, "Type.Secondary");
                        content.Children.Add(desc);
                    }

                    var button = new Button { Content = content };
                    button.SetResourceReference(StyleProperty, "ChoiceButton");
                    System.Windows.Automation.AutomationProperties.SetName(button, choices[i].Title);
                    button.Click += (s, e) =>
                    {
                        ChoiceIndex = index;
                        Confirmed = true;
                        DialogResult = true;
                    };
                    ChoicePanel.Children.Add(button);
                }
            }

            // With choices the choices are the actions; only a cancel button remains.
            PrimaryButton.Content = primaryText ?? "OK";
            PrimaryButton.Visibility = choices != null && choices.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            SecondaryButton.Content = secondaryText ?? "Loobu";
            SecondaryButton.Visibility = secondaryText == null ? Visibility.Collapsed : Visibility.Visible;
            // Esc must close even when there is only one button.
            if (secondaryText == null) PrimaryButton.IsCancel = true;
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            DialogResult = true;
        }

        private void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Drag by any empty part of the card.
            if (e.OriginalSource is TextBox || e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Shows themed dialogs, owned by the Family Library window (or Revit's main window when it is hidden).</summary>
    public static class LibraryDialogs
    {
        public static void Info(string title, string message, string details = null, DialogKind kind = DialogKind.Info)
            => Show(kind, title, message, details, null, "OK", null);

        public static void Error(string title, string message, string details = null)
            => Show(DialogKind.Error, title, message, details, null, "OK", null);

        /// <summary>Command-link choice. Returns the chosen index, or -1 when cancelled.</summary>
        public static int Choose(string title, string message, IList<DialogChoice> choices, string details = null, string cancelText = "Loobu")
        {
            var dlg = Show(DialogKind.Question, title, message, details, choices, null, cancelText);
            return dlg != null && dlg.Confirmed ? dlg.ChoiceIndex : -1;
        }

        private static LibraryDialog Show(DialogKind kind, string title, string message, string details,
                                          IList<DialogChoice> choices, string primaryText, string secondaryText)
        {
            var dlg = new LibraryDialog();
            dlg.Setup(kind, title, message, details, choices, primaryText, secondaryText);

            var owner = Revit.UiWindowHost.DialogOwnerWindow;
            if (owner != null)
                dlg.Owner = owner;
            else if (Revit.UiWindowHost.RevitHandle != IntPtr.Zero)
                new WindowInteropHelper(dlg).Owner = Revit.UiWindowHost.RevitHandle;
            else
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dlg.ShowDialog();
            return dlg;
        }
    }
}
