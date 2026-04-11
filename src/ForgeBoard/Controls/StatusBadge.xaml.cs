using ForgeBoard.Contracts.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace ForgeBoard.Controls;

public sealed partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status),
        typeof(BuildStatus),
        typeof(StatusBadge),
        new PropertyMetadata(BuildStatus.Queued, OnStatusChanged)
    );

    public BuildStatus Status
    {
        get => (BuildStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public StatusBadge()
    {
        this.InitializeComponent();
        UpdateBadge(BuildStatus.Queued);
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        StatusBadge badge = (StatusBadge)d;
        badge.UpdateBadge((BuildStatus)e.NewValue);
    }

    private void UpdateBadge(BuildStatus status)
    {
        SolidColorBrush background;
        string text;

        switch (status)
        {
            case BuildStatus.Succeeded:
                background = new SolidColorBrush(ColorHelper.FromArgb(255, 76, 175, 80));
                text = "Succeeded";
                break;
            case BuildStatus.Failed:
                background = new SolidColorBrush(ColorHelper.FromArgb(255, 244, 67, 54));
                text = "Failed";
                break;
            case BuildStatus.Running:
                background = new SolidColorBrush(ColorHelper.FromArgb(255, 33, 150, 243));
                text = "Running";
                break;
            case BuildStatus.Preparing:
                background = new SolidColorBrush(ColorHelper.FromArgb(255, 33, 150, 243));
                text = "Preparing";
                break;
            case BuildStatus.Cancelled:
                background = new SolidColorBrush(ColorHelper.FromArgb(255, 158, 158, 158));
                text = "Cancelled";
                break;
            default:
                background = new SolidColorBrush(ColorHelper.FromArgb(255, 158, 158, 158));
                text = "Queued";
                break;
        }

        BadgeBorder.Background = background;
        BadgeText.Text = text;
        BadgeText.Foreground = new SolidColorBrush(Colors.White);
    }
}
