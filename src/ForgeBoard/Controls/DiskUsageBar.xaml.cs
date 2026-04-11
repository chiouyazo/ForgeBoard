using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Controls;

public sealed partial class DiskUsageBar : UserControl
{
    public static readonly DependencyProperty DiskUsageProperty = DependencyProperty.Register(
        nameof(DiskUsage),
        typeof(DiskUsageInfo),
        typeof(DiskUsageBar),
        new PropertyMetadata(null, OnDiskUsageChanged)
    );

    public DiskUsageInfo? DiskUsage
    {
        get => (DiskUsageInfo?)GetValue(DiskUsageProperty);
        set => SetValue(DiskUsageProperty, value);
    }

    public DiskUsageBar()
    {
        this.InitializeComponent();
    }

    private static void OnDiskUsageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DiskUsageBar bar = (DiskUsageBar)d;
        bar.UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (DiskUsage == null)
            return;

        long driveTotal = DiskUsage.DriveTotalBytes;
        if (driveTotal <= 0)
            driveTotal = Math.Max(DiskUsage.TotalSizeBytes, 1);

        double cacheRatio = (double)DiskUsage.CacheSizeBytes / driveTotal;
        double artifactRatio = (double)DiskUsage.ArtifactSizeBytes / driveTotal;
        double workingRatio = (double)DiskUsage.WorkingSizeBytes / driveTotal;
        double freeRatio = (double)DiskUsage.DriveFreeBytes / driveTotal;
        if (freeRatio < 0)
            freeRatio = 0;

        CacheColumn.Width = new GridLength(Math.Max(cacheRatio, 0.01), GridUnitType.Star);
        ArtifactColumn.Width = new GridLength(Math.Max(artifactRatio, 0.01), GridUnitType.Star);
        WorkingColumn.Width = new GridLength(Math.Max(workingRatio, 0.01), GridUnitType.Star);
        FreeColumn.Width = new GridLength(Math.Max(freeRatio, 0.01), GridUnitType.Star);

        CacheLabel.Text = $"Cache: {FormatBytes(DiskUsage.CacheSizeBytes)}";
        ArtifactLabel.Text = $"Artifacts: {FormatBytes(DiskUsage.ArtifactSizeBytes)}";
        WorkingLabel.Text = $"Working: {FormatBytes(DiskUsage.WorkingSizeBytes)}";
        FreeLabel.Text = $"Free: {FormatBytes(DiskUsage.DriveFreeBytes)}";
        TotalLabel.Text = $"Total used: {FormatBytes(DiskUsage.TotalSizeBytes)}";

        if (DiskUsage.DriveTotalBytes > 0)
        {
            DriveLabel.Text =
                $"Drive: {FormatBytes(DiskUsage.DriveFreeBytes)} free of {FormatBytes(DiskUsage.DriveTotalBytes)}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        double gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1.0)
        {
            return $"{gb:F1} GB";
        }
        double mb = bytes / (1024.0 * 1024.0);
        return $"{mb:F0} MB";
    }
}
