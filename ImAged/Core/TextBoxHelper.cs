using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ImAged.Core
{
    public static class TextBoxHelper
    {
        #region Placeholder Property
        public static string GetPlaceholder(DependencyObject obj) =>
            (string)obj.GetValue(PlaceholderProperty);

        public static void SetPlaceholder(DependencyObject obj, string value) =>
            obj.SetValue(PlaceholderProperty, value);

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.RegisterAttached(
                "Placeholder",
                typeof(string),
                typeof(TextBoxHelper),
                new FrameworkPropertyMetadata(
                    defaultValue: null,
                    propertyChangedCallback: OnPlaceholderChanged)
                );

        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBoxControl)
            {
                if (!textBoxControl.IsLoaded)
                {
                    textBoxControl.Loaded -= TextBoxControl_Loaded;
                    textBoxControl.Loaded += TextBoxControl_Loaded;
                }

                textBoxControl.TextChanged -= TextBoxControl_TextChanged;
                textBoxControl.TextChanged += TextBoxControl_TextChanged;
                
                textBoxControl.GotFocus -= TextBoxControl_GotFocus;
                textBoxControl.GotFocus += TextBoxControl_GotFocus;

                textBoxControl.LostFocus -= TextBoxControl_LostFocus;
                textBoxControl.LostFocus += TextBoxControl_LostFocus;

                if (GetOrCreateAdorner(textBoxControl, out PlaceholderAdorner adorner))
                    adorner.InvalidateVisual();
            }
        }
        #endregion

        #region AutoFocusNext Property
        public static bool GetAutoFocusNext(DependencyObject obj) =>
            (bool)obj.GetValue(AutoFocusNextProperty);

        public static void SetAutoFocusNext(DependencyObject obj, bool value) =>
            obj.SetValue(AutoFocusNextProperty, value);

        public static readonly DependencyProperty AutoFocusNextProperty =
            DependencyProperty.RegisterAttached(
                "AutoFocusNext",
                typeof(bool),
                typeof(TextBoxHelper),
                new FrameworkPropertyMetadata(
                    defaultValue: false,
                    propertyChangedCallback: OnAutoFocusNextChanged)
                );

        private static void OnAutoFocusNextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBoxControl)
            {
                if ((bool)e.NewValue)
                {
                    textBoxControl.TextChanged += TextBoxControl_AutoFocusTextChanged;
                }
                else
                {
                    textBoxControl.TextChanged -= TextBoxControl_AutoFocusTextChanged;
                }
            }
        }
        #endregion

        #region Event Handlers
        private static void TextBoxControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBoxControl)
            {
                textBoxControl.Loaded -= TextBoxControl_Loaded;
                GetOrCreateAdorner(textBoxControl, out _);
            }
        }

        private static void TextBoxControl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBoxControl
                && GetOrCreateAdorner(textBoxControl, out PlaceholderAdorner adorner))
            {
                if (textBoxControl.Text.Length > 0)
                    adorner.Visibility = Visibility.Hidden;
                else
                    adorner.Visibility = Visibility.Visible;
            }
        }

        private static void TextBoxControl_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBoxControl)
            {
                if (!string.IsNullOrEmpty(textBoxControl.Text))
                {
                    textBoxControl.CaretIndex = textBoxControl.Text.Length;
                }

                if (GetOrCreateAdorner(textBoxControl, out PlaceholderAdorner adorner))
                {
                    adorner.InvalidateVisual();
                    adorner.Visibility = Visibility.Hidden;
                }
            }
        }

        private static void TextBoxControl_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBoxControl)
            {
                if (string.IsNullOrEmpty(textBoxControl.Text) &&
                    GetOrCreateAdorner(textBoxControl, out PlaceholderAdorner adorner))
                {
                    adorner.Visibility = Visibility.Visible;
                    adorner.InvalidateVisual();
                }
            }
        }

        private static void TextBoxControl_AutoFocusTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBoxControl)
            {
                if (textBoxControl.Text.Length >= textBoxControl.MaxLength)
                {
                    MoveToNextField(textBoxControl);
                }
            }
        }
        #endregion

        #region Helper Methods
        private static bool GetOrCreateAdorner(TextBox textBoxControl, out PlaceholderAdorner adorner)
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(textBoxControl);

            if (layer == null)
            {
                adorner = null;
                return false;
            }

            adorner = layer.GetAdorners(textBoxControl)?.OfType<PlaceholderAdorner>().FirstOrDefault();

            if (adorner == null)
            {
                adorner = new PlaceholderAdorner(textBoxControl);
                layer.Add(adorner);
            }

            return true;
        }

        private static void MoveToNextField(TextBox currentTextBox)
        {
            var parent = currentTextBox.Parent as Panel;
            if (parent == null) return;

            var textBoxes = parent.Children.OfType<TextBox>().Where(tb => GetAutoFocusNext(tb)).ToArray();

            int currentIndex = Array.IndexOf(textBoxes, currentTextBox);
            if (currentIndex >= 0 && currentIndex < textBoxes.Length - 1)
            {
                textBoxes[currentIndex + 1].Focus();
            }
        }
        #endregion

        #region PlaceholderAdorner
        public class PlaceholderAdorner : Adorner
        {
            public PlaceholderAdorner(TextBox textBox) : base(textBox) 
            {
                this.IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                TextBox textBoxControl = (TextBox)AdornedElement;
                string placeholderValue = TextBoxHelper.GetPlaceholder(textBoxControl);

                if (string.IsNullOrEmpty(placeholderValue))
                    return;

                if (textBoxControl.IsFocused)
                    return;

                FormattedText text = new FormattedText(
                    placeholderValue,
                    System.Globalization.CultureInfo.CurrentCulture,
                    textBoxControl.FlowDirection,
                    new Typeface(textBoxControl.FontFamily,
                                 textBoxControl.FontStyle,
                                 textBoxControl.FontWeight,
                                 textBoxControl.FontStretch),
                    textBoxControl.FontSize,
                    new SolidColorBrush(Color.FromRgb(158, 158, 158)), 
                    VisualTreeHelper.GetDpi(textBoxControl).PixelsPerDip);

                double availableWidth = textBoxControl.ActualWidth;
                double availableHeight = textBoxControl.ActualHeight;

                if (textBoxControl.Template.FindName("PART_ContentHost", textBoxControl) is FrameworkElement contentHost)
                {
                    Point contentHostPosition = contentHost.TransformToAncestor(textBoxControl).Transform(new Point(0, 0));
                    
                    availableWidth = contentHost.ActualWidth;
                    availableHeight = contentHost.ActualHeight;
                    
                    Point renderingOffset = new Point(contentHostPosition.X, contentHostPosition.Y);
                    
                    double textWidth = Math.Min(text.Width, availableWidth);
                    double textHeight = Math.Min(text.Height, availableHeight);
                    
                    renderingOffset.X += (availableWidth - textWidth) / 2;
                    renderingOffset.Y += (availableHeight - textHeight) / 2;

                    renderingOffset.X += -6;

                    drawingContext.DrawText(text, renderingOffset);
                }
                else
                {
                    // Fallback if PART_ContentHost is not found
                    Point renderingOffset = new Point(5, (availableHeight - text.Height) / 2);
                    drawingContext.DrawText(text, renderingOffset);
                }
            }
        }
        #endregion
    }
}