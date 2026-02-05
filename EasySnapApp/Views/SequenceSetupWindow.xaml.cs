using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EasySnapApp.Views
{
    public partial class SequenceSetupWindow : Window
    {
        private bool _isLoading = true;

        public SequenceSetupWindow()
        {
            InitializeComponent();
            LoadCurrentSettings();
            UpdatePreview();
            _isLoading = false;
        }

        private void LoadCurrentSettings()
        {
            // Load from settings (default values are set in XAML)
            var digits = Properties.Settings.Default.SequenceDigits;
            var startNum = Properties.Settings.Default.SequenceStartNumber;
            var increment = Properties.Settings.Default.SequenceIncrement;

            // Set digits dropdown
            var digitsItem = cmbDigits.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Content.ToString() == digits.ToString());
            if (digitsItem != null)
                cmbDigits.SelectedItem = digitsItem;

            // Set starting number
            txtStartNumber.Text = startNum.ToString();

            // Set increment (now a text box instead of dropdown)
            txtIncrement.Text = increment.ToString();
        }

        private void Settings_Changed(object sender, EventArgs e)
        {
            if (!_isLoading)
            {
                UpdatePreview();
                ValidateSettings();
            }
        }

        private void UpdatePreview()
        {
            try
            {
                var digits = int.Parse(((ComboBoxItem)cmbDigits.SelectedItem).Content.ToString());
                var startNum = int.Parse(txtStartNumber.Text);
                var increment = int.Parse(txtIncrement.Text);

                // Generate preview sequence
                var preview = string.Join(", ",
                    Enumerable.Range(0, 5).Select(i => startNum + (i * increment)));

                txtPreview.Text = preview;

                // Update parsing info
                runDigits.Text = digits.ToString();
                var exampleSeq = startNum.ToString().PadLeft(digits, '0');
                runFileExample.Text = $"07.0975.987GT.{exampleSeq}.jpg";

                // Enable save button if settings are valid
                btnSave.IsEnabled = IsValidSettings();
            }
            catch
            {
                txtPreview.Text = "Invalid settings";
                btnSave.IsEnabled = false;
            }
        }

        private void ValidateSettings()
        {
            try
            {
                var warnings = "";
                var digits = int.Parse(((ComboBoxItem)cmbDigits.SelectedItem).Content.ToString());
                var startNum = int.Parse(txtStartNumber.Text);
                var increment = int.Parse(txtIncrement.Text);

                // Check if starting number fits in digit count
                var maxValue = (int)Math.Pow(10, digits) - 1;
                if (startNum > maxValue)
                {
                    warnings += $"• Starting number {startNum} too large for {digits} digits (max: {maxValue})\n";
                }

                // Check if increment is reasonable relative to digit space
                var availableSpace = maxValue - startNum;
                if (increment > availableSpace / 2)
                {
                    warnings += $"• Large increment ({increment}) may quickly exceed {digits}-digit limit\n";
                }

                // Check for overflow in first few sequences
                var fifthSequence = startNum + (4 * increment);
                if (fifthSequence > maxValue)
                {
                    warnings += $"• Sequence will exceed {digits}-digit limit after {CalculateSequencesThatFit(startNum, increment, maxValue)} images\n";
                }

                // Check for zero or negative increment
                if (increment <= 0)
                {
                    warnings += "• Increment must be positive\n";
                }

                // Check for unreasonably large increment
                if (increment > 1000)
                {
                    warnings += "• Very large increment - consider smaller value\n";
                }

                // Show/hide warnings
                if (!string.IsNullOrEmpty(warnings))
                {
                    txtWarnings.Text = "⚠ Warnings:\n" + warnings.Trim();
                    borderWarnings.Visibility = Visibility.Visible;
                }
                else
                {
                    borderWarnings.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                borderWarnings.Visibility = Visibility.Collapsed;
            }
        }

        private int CalculateSequencesThatFit(int start, int increment, int maxValue)
        {
            int count = 1;
            int current = start;
            while (current + increment <= maxValue)
            {
                current += increment;
                count++;
            }
            return count;
        }

        private bool IsValidSettings()
        {
            try
            {
                var digits = int.Parse(((ComboBoxItem)cmbDigits.SelectedItem).Content.ToString());
                var startNum = int.Parse(txtStartNumber.Text);
                var increment = int.Parse(txtIncrement.Text);

                // Basic validation
                if (digits < 2 || digits > 5) return false;
                if (startNum <= 0) return false;
                if (increment <= 0) return false;

                // Check if starting number fits in digit count
                var maxValue = (int)Math.Pow(10, digits) - 1;
                if (startNum > maxValue) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }

        private void ShowRules_Click(object sender, RoutedEventArgs e)
        {
            var rulesMessage = "Sequence Number Rules & Limits:\n\n" +
                "• 2 digits: Sequences 10-99 (90 total)\n" +
                "• 3 digits: Sequences 100-999 (900 total)\n" +
                "• 4 digits: Sequences 1000-9999 (9,000 total)\n" +
                "• 5 digits: Sequences 10000-99999 (90,000 total)\n\n" +
                "Best Practices:\n" +
                "• Use increments of 2-10 for most cases\n" +
                "• Start with 103, 1003, etc. to avoid low numbers\n" +
                "• Large increments waste sequence space\n" +
                "• Plan for future image insertions\n\n" +
                "Overflow Handling:\n" +
                "When sequences exceed the digit limit, the app will:\n" +
                "• Log a warning in the session\n" +
                "• Continue with the actual number (may break parsing)\n" +
                "• Recommend increasing digit count";

            MessageBox.Show(rulesMessage, "Sequence Rules & Limits",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var digits = int.Parse(((ComboBoxItem)cmbDigits.SelectedItem).Content.ToString());
                var startNum = int.Parse(txtStartNumber.Text);
                var increment = int.Parse(txtIncrement.Text);

                // Final validation
                if (!IsValidSettings())
                {
                    MessageBox.Show("Please check your settings. Make sure all values are valid and the starting number fits within the specified digit count.",
                                    "Invalid Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Confirm potentially problematic settings
                var maxValue = (int)Math.Pow(10, digits) - 1;
                var sequencesThatFit = CalculateSequencesThatFit(startNum, increment, maxValue);

                if (sequencesThatFit < 20)
                {
                    var result = MessageBox.Show($"Warning: With these settings, you can only capture {sequencesThatFit} images before exceeding the {digits}-digit limit.\n\nDo you want to continue anyway?",
                        "Limited Sequence Space", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.No)
                        return;
                }

                // Save to settings
                Properties.Settings.Default.SequenceDigits = digits;
                Properties.Settings.Default.SequenceStartNumber = startNum;
                Properties.Settings.Default.SequenceIncrement = increment;
                Properties.Settings.Default.Save();

                MessageBox.Show("Sequence settings saved successfully!", "Settings Saved",
                               MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Reset to default settings?\n\n• 3 digits\n• Start at 103\n• Increment by 2",
                                        "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _isLoading = true;

                // Reset to defaults
                cmbDigits.SelectedIndex = 1; // 3 digits
                txtStartNumber.Text = "103";
                txtIncrement.Text = "2";  // Now a text box

                _isLoading = false;
                UpdatePreview();
                ValidateSettings();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}