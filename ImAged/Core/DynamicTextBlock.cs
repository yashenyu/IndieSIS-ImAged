using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImAged.Core
{
    public class DynamicTextBlock : TextBlock
    {
        public static readonly DependencyProperty FullTextProperty =
            DependencyProperty.Register("FullText", typeof(string), typeof(DynamicTextBlock),
                new PropertyMetadata(string.Empty, OnFullTextChanged));

        public string FullText
        {
            get { return (string)GetValue(FullTextProperty); }
            set { SetValue(FullTextProperty, value); }
        }

        private static void OnFullTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBlock = d as DynamicTextBlock;
            textBlock?.UpdateText();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateText();
        }

        private void UpdateText()
        {
            if (string.IsNullOrEmpty(FullText))
            {
                Text = string.Empty;
                return;
            }

            var availableWidth = ActualWidth - Padding.Left - Padding.Right;
            var availableHeight = ActualHeight - Padding.Top - Padding.Bottom;

            if (availableWidth <= 0 || availableHeight <= 0)
            {
                Text = FullText;
                return;
            }

            var tempTextBlock = new TextBlock
            {
                FontFamily = FontFamily,
                FontSize = FontSize,
                FontWeight = FontWeight,
                FontStyle = FontStyle,
                TextWrapping = TextWrapping.Wrap
            };

            var left = 0;
            var right = FullText.Length;
            var bestLength = 0;

            while (left <= right)
            {
                var mid = (left + right) / 2;
                var testText = mid < FullText.Length ? FullText.Substring(0, mid) + "..." : FullText;
                tempTextBlock.Text = testText;

                tempTextBlock.Measure(new Size(availableWidth, double.PositiveInfinity));
                var requiredHeight = tempTextBlock.DesiredSize.Height;

                if (requiredHeight <= availableHeight)
                {
                    bestLength = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            if (bestLength >= FullText.Length)
            {
                Text = FullText;
            }
            else
            {
                Text = FullText.Substring(0, bestLength) + "...";
            }
        }
    }
}