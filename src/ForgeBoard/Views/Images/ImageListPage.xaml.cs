using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;
using ForgeBoard.ViewModels;

namespace ForgeBoard.Views.Images;

public sealed partial class ImageListPage : Page
{
    private readonly ImageListViewModel _viewModel;
    private DispatcherTimer? _notificationTimer;

    public ImageListPage()
    {
        this.InitializeComponent();
        _viewModel = new ImageListViewModel(App.ApiClient);
        this.DataContext = _viewModel;
        this.Loaded += async (s, e) => await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.FeedTestCompleted += OnFeedTestCompleted;
    }

    private void OnFeedTestCompleted(string feedId, string statusText, bool success)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            UpdateFeedTestStatusInTree(this, feedId, statusText, success);

            if (statusText != "Testing...")
            {
                DispatcherTimer clearTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3),
                };
                string capturedFeedId = feedId;
                clearTimer.Tick += (s, e) =>
                {
                    clearTimer.Stop();
                    UpdateFeedTestStatusInTree(this, capturedFeedId, string.Empty, false);
                };
                clearTimer.Start();
            }
        });
    }

    private static void UpdateFeedTestStatusInTree(
        DependencyObject parent,
        string feedId,
        string statusText,
        bool success
    )
    {
        int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (
                child is TextBlock textBlock
                && textBlock.Name == "FeedTestStatusText"
                && textBlock.Tag is string tagId
                && tagId == feedId
            )
            {
                if (string.IsNullOrEmpty(statusText))
                {
                    textBlock.Visibility = Visibility.Collapsed;
                    textBlock.Text = string.Empty;
                    return;
                }

                textBlock.Visibility = Visibility.Visible;
                if (statusText == "Testing...")
                {
                    textBlock.Text = "Testing...";
                    textBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources["MutedBrush"];
                }
                else if (success)
                {
                    textBlock.Text = "\u2713 Connected";
                    textBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources["SuccessBrush"];
                }
                else
                {
                    textBlock.Text = "\u2717 Failed";
                    textBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources["ErrorBrush"];
                }
                return;
            }
            UpdateFeedTestStatusInTree(child, feedId, statusText, success);
        }
    }

    private void ShowNotification(string message)
    {
        NotificationText.Text = message;
        NotificationBar.Visibility = Visibility.Visible;

        _notificationTimer?.Stop();
        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _notificationTimer.Tick += (s, e) =>
        {
            NotificationBar.Visibility = Visibility.Collapsed;
            _notificationTimer.Stop();
        };
        _notificationTimer.Start();
    }

    private void DismissNotification_Click(object sender, RoutedEventArgs e)
    {
        NotificationBar.Visibility = Visibility.Collapsed;
        _notificationTimer?.Stop();
    }

    private void AddLocalImage_Click(object sender, RoutedEventArgs e)
    {
        LocalImagePanel.Visibility =
            LocalImagePanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void CancelLocalImage_Click(object sender, RoutedEventArgs e)
    {
        LocalImagePanel.Visibility = Visibility.Collapsed;
    }

    private async void ImportFromFeed_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Feeds.Count == 0)
        {
            ShowNotification("Configure a feed first in the Manage Feeds panel.");
            return;
        }

        StackPanel content = new StackPanel { Spacing = 12, MinWidth = 500 };

        ComboBox feedCombo = new ComboBox
        {
            Header = "Select Feed",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
            DisplayMemberPath = "Name",
        };
        foreach (Feed feed in _viewModel.Feeds)
        {
            feedCombo.Items.Add(feed);
        }
        if (feedCombo.Items.Count > 0)
        {
            feedCombo.SelectedIndex = 0;
        }

        TextBlock browseStatus = new TextBlock
        {
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
        };

        ListView imageList = new ListView
        {
            SelectionMode = Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single,
            MaxHeight = 300,
            Visibility = Visibility.Collapsed,
        };

        Button browseButton = new Button
        {
            Content = "Browse Feed",
            Margin = new Thickness(0, 4, 0, 0),
        };
        browseButton.Click += async (s, args) =>
        {
            if (feedCombo.SelectedItem is not Feed selectedFeed)
            {
                return;
            }
            browseStatus.Text = "Loading images...";
            browseStatus.Visibility = Visibility.Visible;
            imageList.Items.Clear();
            imageList.Visibility = Visibility.Collapsed;
            try
            {
                List<FeedImage> images = await App.ApiClient.BrowseFeedAsync(selectedFeed.Id);
                browseStatus.Visibility = Visibility.Collapsed;
                if (images.Count == 0)
                {
                    browseStatus.Text = "No images found in this feed.";
                    browseStatus.Visibility = Visibility.Visible;
                    return;
                }
                foreach (FeedImage image in images)
                {
                    Grid row = new Grid();
                    row.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    );
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Tag = image.Path;

                    TextBlock nameBlock = new TextBlock
                    {
                        Text = image.Name,
                        FontSize = 13,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)
                            Application.Current.Resources["TextBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    Grid.SetColumn(nameBlock, 0);
                    row.Children.Add(nameBlock);

                    Border formatBadge = new Border
                    {
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = (Microsoft.UI.Xaml.Media.Brush)
                            Application.Current.Resources["AccentBrush"],
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    formatBadge.Child = new TextBlock
                    {
                        Text = image.Format.ToUpperInvariant(),
                        FontSize = 10,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.White
                        ),
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    };
                    Grid.SetColumn(formatBadge, 1);
                    row.Children.Add(formatBadge);

                    TextBlock sizeBlock = new TextBlock
                    {
                        Text = ForgeBoard.Converters.FileSizeConverter.FormatFileSize(
                            image.SizeBytes
                        ),
                        FontSize = 11,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)
                            Application.Current.Resources["HintBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                    };
                    Grid.SetColumn(sizeBlock, 2);
                    row.Children.Add(sizeBlock);

                    if (!string.IsNullOrEmpty(image.Version))
                    {
                        TextBlock versionBlock = new TextBlock
                        {
                            Text = $"v{image.Version}",
                            FontSize = 11,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)
                                Application.Current.Resources["MutedBrush"],
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(8, 0, 0, 0),
                        };
                        Grid.SetColumn(versionBlock, 3);
                        row.Children.Add(versionBlock);
                    }

                    imageList.Items.Add(row);
                }
                imageList.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                browseStatus.Text = $"Failed to browse feed: {ex.Message}";
                browseStatus.Visibility = Visibility.Visible;
            }
        };

        content.Children.Add(feedCombo);
        content.Children.Add(browseButton);
        content.Children.Add(browseStatus);
        content.Children.Add(imageList);

        ContentDialog dialog = new ContentDialog
        {
            Title = "Import Image from Feed",
            Content = content,
            PrimaryButtonText = "Import Selected",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (
            result == ContentDialogResult.Primary
            && feedCombo.SelectedItem is Feed chosenFeed
            && imageList.SelectedItem is Grid selectedRow
            && selectedRow.Tag is string remotePath
        )
        {
            await _viewModel.ImportFromFeedCommand.ExecuteAsync(
                new ImportRequest { FeedId = chosenFeed.Id, RemotePath = remotePath }
            );
        }
    }

    private async void PreviewBuildSteps_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            ImageListItem? item = _viewModel.FilteredImages.FirstOrDefault(i => i.Id == id);
            if (item?.BuildDefinitionId is null)
            {
                Shell.Current?.ShowWarning("No build definition linked to this artifact.");
                return;
            }

            try
            {
                BuildDefinition definition = await App.ApiClient.GetBuildDefinitionAsync(
                    item.BuildDefinitionId
                );
                BuildPreviewResult preview = await App.ApiClient.PreviewBuildAsync(definition);

                List<string> stepFlow = new List<string>();
                if (preview.Steps.Count > 0)
                {
                    stepFlow.AddRange(preview.Steps);
                }
                else
                {
                    foreach (BuildStep step in definition.Steps)
                    {
                        stepFlow.Add($"{step.Name} ({step.StepType})");
                    }
                }

                StackPanel flowPanel = new StackPanel { Spacing = 6 };
                for (int i = 0; i < stepFlow.Count; i++)
                {
                    TextBlock stepText = new TextBlock
                    {
                        Text = $"{i + 1}. {stepFlow[i]}",
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                    };
                    flowPanel.Children.Add(stepText);
                }

                ScrollViewer scrollViewer = new ScrollViewer
                {
                    Content = flowPanel,
                    MaxHeight = 400,
                };

                ContentDialog dialog = new ContentDialog
                {
                    Title = $"Build Steps: {definition.Name}",
                    Content = scrollViewer,
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    MinWidth = 500,
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Shell.Current?.ShowError($"Failed to load build steps: {ex.Message}");
            }
        }
    }

    private async void PublishImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            ImageListItem? item = _viewModel.FilteredImages.FirstOrDefault(i => i.Id == id);
            string? artifactId = item?.ArtifactId;

            if (string.IsNullOrEmpty(artifactId))
            {
                Shell.Current?.ShowWarning("Only build artifacts can be published.");
                return;
            }

            List<Feed> feeds;
            try
            {
                feeds = await App.ApiClient.GetFeedsAsync();
            }
            catch
            {
                Shell.Current?.ShowError("Failed to load feeds.");
                return;
            }

            if (feeds.Count == 0)
            {
                Shell.Current?.ShowWarning("No feeds configured. Add a feed first.");
                return;
            }

            StackPanel publishContent = new StackPanel { Spacing = 12, MinWidth = 400 };

            ComboBox feedSelector = new ComboBox
            {
                Header = "Destination Feed",
                ItemsSource = feeds.Select(f => f.Name).ToList(),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            publishContent.Children.Add(feedSelector);

            // Repository picker for Nexus feeds
            ComboBox repoSelector = new ComboBox
            {
                Header = "Repository",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = Visibility.Collapsed,
            };
            publishContent.Children.Add(repoSelector);

            // Load repos when feed changes
            feedSelector.SelectionChanged += async (s, args) =>
            {
                int idx = feedSelector.SelectedIndex;
                if (idx >= 0 && idx < feeds.Count && feeds[idx].SourceType == FeedType.Nexus)
                {
                    repoSelector.Visibility = Visibility.Visible;
                    try
                    {
                        List<string> repos = await App.ApiClient.GetFeedRepositoriesAsync(
                            feeds[idx].Id
                        );
                        repoSelector.ItemsSource = repos;
                        string currentRepo = feeds[idx].Repository ?? string.Empty;
                        int repoIdx = repos.IndexOf(currentRepo);
                        repoSelector.SelectedIndex = repoIdx >= 0 ? repoIdx : 0;
                    }
                    catch
                    {
                        repoSelector.ItemsSource = new List<string>();
                    }
                }
                else
                {
                    repoSelector.Visibility = Visibility.Collapsed;
                }
            };

            // Trigger initial load
            if (feeds.Count > 0 && feeds[0].SourceType == FeedType.Nexus)
            {
                feedSelector.SelectedIndex = -1;
                feedSelector.SelectedIndex = 0;
            }

            TextBox versionBox = new TextBox
            {
                Header = "Version",
                Text = "1.0.0",
                PlaceholderText = "1.0.0",
            };
            publishContent.Children.Add(versionBox);

            ComboBox formatSelector = new ComboBox
            {
                Header = "Convert To",
                ItemsSource = new List<string>
                {
                    "Same (no conversion)",
                    ".box (Vagrant)",
                    ".vhdx (standalone)",
                },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            publishContent.Children.Add(formatSelector);

            TextBox notesBox = new TextBox
            {
                Header = "Release Notes (optional)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 120,
                PlaceholderText = "What changed in this build...",
            };
            publishContent.Children.Add(notesBox);

            ContentDialog dialog = new ContentDialog
            {
                Title = $"Publish: {item?.Name ?? "Artifact"}",
                Content = publishContent,
                PrimaryButtonText = "Publish",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            int selectedIndex = feedSelector.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= feeds.Count)
                return;

            Feed selectedFeed = feeds[selectedIndex];

            try
            {
                string? convertFormat = formatSelector.SelectedIndex switch
                {
                    1 => "box",
                    2 => "vhdx",
                    _ => null,
                };

                string? selectedRepo = repoSelector.SelectedItem as string;

                PublishRequest publishRequest = new PublishRequest
                {
                    FeedId = selectedFeed.Id,
                    Repository = selectedRepo,
                    Version = versionBox.Text,
                    ReleaseNotes = notesBox.Text,
                    ConvertFormat = convertFormat,
                };

                await App.ApiClient.PublishArtifactAsync(artifactId, publishRequest);
                Shell.Current?.ShowNotification(
                    "Publishing started. Track progress on the Dashboard."
                );
            }
            catch (Exception ex)
            {
                Shell.Current?.ShowError($"Failed to start publish: {ex.Message}");
            }
        }
    }

    private async void DeleteImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            ImageListItem? image = _viewModel.FilteredImages.FirstOrDefault(i => i.Id == id);
            string name = image?.Name ?? id;

            ContentDialog dialog = new ContentDialog
            {
                Title = "Confirm Delete",
                Content = $"Delete '{name}'? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            await _viewModel.DeleteImageCommand.ExecuteAsync(id);
        }
    }

    private async void LaunchVm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            try
            {
                button.IsEnabled = false;
                button.Content = "Creating...";
                string response = await App.ApiClient.LaunchVmAsync(id);
                Shell.Current?.ShowNotification("VM created and started");
            }
            catch (Exception ex)
            {
                Shell.Current?.ShowError($"Failed to launch VM: {ex.Message}");
            }
            finally
            {
                button.IsEnabled = true;
                button.Content = "Launch VM";
            }
        }
    }

    private async void AddFeed_Click(object sender, RoutedEventArgs e)
    {
        StackPanel content = new StackPanel { Spacing = 12 };

        ComboBox typeCombo = new ComboBox
        {
            Header = "Feed Type",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
        };
        foreach (FeedType feedType in Enum.GetValues<FeedType>())
        {
            typeCombo.Items.Add(feedType.ToString());
        }
        typeCombo.SelectedIndex = 0;

        TextBox nameBox = new TextBox
        {
            Header = "Name",
            PlaceholderText = "Feed name",
            FontSize = 13,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
        };

        TextBox urlBox = new TextBox
        {
            Header = "Connection URL / Path",
            PlaceholderText = "URL, UNC path, or local path",
            FontSize = 13,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
        };

        TextBox usernameBox = new TextBox
        {
            Header = "Username (optional)",
            PlaceholderText = "Username",
            FontSize = 13,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
        };

        PasswordBox passwordBox = new PasswordBox
        {
            Header = "Password (optional)",
            PlaceholderText = "Password",
            FontSize = 13,
        };

        content.Children.Add(typeCombo);
        content.Children.Add(nameBox);
        content.Children.Add(urlBox);
        content.Children.Add(usernameBox);
        content.Children.Add(passwordBox);

        ContentDialog dialog = new ContentDialog
        {
            Title = "Add Feed",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            FeedType parsedType = FeedType.LocalPath;
            if (typeCombo.SelectedItem is string typeStr)
            {
                Enum.TryParse(typeStr, out parsedType);
            }

            Feed feed = new Feed
            {
                Name = nameBox.Text?.Trim() ?? string.Empty,
                SourceType = parsedType,
                ConnectionString = urlBox.Text?.Trim() ?? string.Empty,
                Username = string.IsNullOrWhiteSpace(usernameBox.Text)
                    ? null
                    : usernameBox.Text.Trim(),
                Password = string.IsNullOrWhiteSpace(passwordBox.Password)
                    ? null
                    : passwordBox.Password,
            };

            await _viewModel.AddFeedCommand.ExecuteAsync(feed);
        }
    }

    private async void EditFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            Feed? existing = _viewModel.Feeds.FirstOrDefault(f => f.Id == id);
            if (existing is null)
                return;

            StackPanel content = new StackPanel { Spacing = 12 };

            ComboBox typeCombo = new ComboBox
            {
                Header = "Feed Type",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 13,
            };
            foreach (FeedType feedType in Enum.GetValues<FeedType>())
            {
                typeCombo.Items.Add(feedType.ToString());
            }
            typeCombo.SelectedItem = existing.SourceType.ToString();

            TextBox nameBox = new TextBox
            {
                Header = "Name",
                Text = existing.Name,
                FontSize = 13,
                AcceptsReturn = false,
                TextWrapping = TextWrapping.NoWrap,
            };

            TextBox urlBox = new TextBox
            {
                Header = "Connection URL / Path",
                Text = existing.ConnectionString,
                FontSize = 13,
                AcceptsReturn = false,
                TextWrapping = TextWrapping.NoWrap,
            };

            TextBox usernameBox = new TextBox
            {
                Header = "Username (optional)",
                Text = existing.Username ?? string.Empty,
                FontSize = 13,
                AcceptsReturn = false,
                TextWrapping = TextWrapping.NoWrap,
            };

            PasswordBox passwordBox = new PasswordBox
            {
                Header = "Password (optional)",
                Password = existing.Password ?? string.Empty,
                FontSize = 13,
            };

            content.Children.Add(typeCombo);
            content.Children.Add(nameBox);
            content.Children.Add(urlBox);
            content.Children.Add(usernameBox);
            content.Children.Add(passwordBox);

            ContentDialog dialog = new ContentDialog
            {
                Title = "Edit Feed",
                Content = content,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                FeedType parsedType = existing.SourceType;
                if (typeCombo.SelectedItem is string typeStr)
                {
                    Enum.TryParse(typeStr, out parsedType);
                }

                existing.Name = nameBox.Text?.Trim() ?? string.Empty;
                existing.SourceType = parsedType;
                existing.ConnectionString = urlBox.Text?.Trim() ?? string.Empty;
                existing.Username = string.IsNullOrWhiteSpace(usernameBox.Text)
                    ? null
                    : usernameBox.Text.Trim();
                existing.Password = string.IsNullOrWhiteSpace(passwordBox.Password)
                    ? null
                    : passwordBox.Password;

                await _viewModel.EditFeedCommand.ExecuteAsync(existing);
            }
        }
    }

    private async void TestFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            await _viewModel.TestFeedCommand.ExecuteAsync(id);
        }
    }

    private async void DeleteFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            Feed? feed = _viewModel.Feeds.FirstOrDefault(f => f.Id == id);
            string name = feed?.Name ?? id;

            ContentDialog dialog = new ContentDialog
            {
                Title = "Confirm Delete",
                Content = $"Delete feed '{name}'? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            await _viewModel.DeleteFeedCommand.ExecuteAsync(id);
        }
    }

    private async void BrowseLocalImage_Click(object sender, RoutedEventArgs e)
    {
        TextBox pathInput = new TextBox
        {
            PlaceholderText = @"C:\images\base-win11.box",
            Header = "Path to image file (.box, .iso, .vhdx, .qcow2)",
            Text = LocalPathBox.Text,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
        };

        ContentDialog dialog = new ContentDialog
        {
            Title = "Select Image File",
            Content = pathInput,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(pathInput.Text))
        {
            LocalPathBox.Text = pathInput.Text.Trim();
            AutoFillFromPath(pathInput.Text.Trim());
        }
    }

    private void LocalPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string path = LocalPathBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(path))
        {
            AutoFillFromPath(path);
        }
        else
        {
            FileSizeText.Text = string.Empty;
        }
    }

    private void AutoFillFromPath(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.FileInfo fileInfo = new System.IO.FileInfo(path);
                long sizeMb = fileInfo.Length / (1024 * 1024);
                FileSizeText.Text = $"File size: {sizeMb} MB";

                if (string.IsNullOrWhiteSpace(LocalNameBox.Text))
                {
                    LocalNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(path);
                }
            }
            else
            {
                FileSizeText.Text = string.Empty;
            }
        }
        catch
        {
            FileSizeText.Text = string.Empty;
        }
    }
}
