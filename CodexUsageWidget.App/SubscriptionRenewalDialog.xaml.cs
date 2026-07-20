using System.Globalization;
using System.Windows;

namespace CodexUsageWidget.App;

public partial class SubscriptionRenewalDialog : Window
{
    public SubscriptionRenewalDialog(DateTime? currentValue)
    {
        InitializeComponent();
        RenewalDatePicker.SelectedDate = currentValue?.Date ?? DateTime.Today;
        TimeTextBox.Text = currentValue?.ToString("HH:mm") ?? "00:00";
    }

    public DateTime? RenewalAt { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (RenewalDatePicker.SelectedDate is not DateTime date)
        {
            ShowError("日付を入力してください。");
            return;
        }

        if (!TimeSpan.TryParseExact(TimeTextBox.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var time))
        {
            ShowError("時刻は HH:mm 形式で入力してください。例: 09:30");
            return;
        }

        RenewalAt = date.Date + time;
        DialogResult = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        RenewalAt = null;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
