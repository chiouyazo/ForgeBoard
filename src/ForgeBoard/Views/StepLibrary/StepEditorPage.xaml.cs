using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;
using Windows.System;

namespace ForgeBoard.Views.StepLibrary;

public sealed partial class StepEditorPage : Page
{
    private string? _editingStepId;

    public StepEditorPage()
    {
        this.InitializeComponent();
        TypeCombo.SelectedIndex = 0;
        UpdateTypeSpecificPanels("PowerShell");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string stepId && !string.IsNullOrEmpty(stepId))
        {
            _editingStepId = stepId;
            _ = LoadStepAsync(stepId);
        }
    }

    private async Task LoadStepAsync(string stepId)
    {
        try
        {
            BuildStepLibraryEntry step = await App.ApiClient.GetStepAsync(stepId);
            NameBox.Text = step.Name;
            DescriptionBox.Text = step.Description;
            TagsBox.Text = string.Join(", ", step.Tags);
            TimeoutBox.Text = step.DefaultTimeoutSeconds.ToString();

            string typeTag = step.StepType.ToString();
            foreach (object item in TypeCombo.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Tag is string tag && tag == typeTag)
                {
                    TypeCombo.SelectedItem = comboItem;
                    break;
                }
            }

            PopulateContentForType(step.StepType, step.Content);
        }
        catch (Exception)
        {
            _editingStepId = null;
        }
    }

    private void PopulateContentForType(BuildStepType stepType, string content)
    {
        switch (stepType)
        {
            case BuildStepType.PowerShell:
            case BuildStepType.Shell:
                ContentBox.Text = content;
                break;
            case BuildStepType.PowerShellFile:
            case BuildStepType.ShellFile:
                FilePathBox.Text = content;
                break;
            case BuildStepType.FileUpload:
                string[] parts = content.Split("=>", StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    UploadSourceBox.Text = parts[0];
                    UploadDestinationBox.Text = parts[1];
                }
                else
                {
                    UploadSourceBox.Text = content;
                }
                break;
            case BuildStepType.Custom:
                CustomContentBox.Text = content;
                break;
            case BuildStepType.WindowsRestart:
                break;
        }
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            UpdateTypeSpecificPanels(tag);
        }
    }

    private void UpdateTypeSpecificPanels(string typeTag)
    {
        if (ScriptPanel is null)
            return;

        ScriptPanel.Visibility = Visibility.Collapsed;
        FilePathPanel.Visibility = Visibility.Collapsed;
        FileUploadPanel.Visibility = Visibility.Collapsed;
        RestartPanel.Visibility = Visibility.Collapsed;
        CustomPanel.Visibility = Visibility.Collapsed;

        switch (typeTag)
        {
            case "PowerShell":
            case "Shell":
                ScriptPanel.Visibility = Visibility.Visible;
                break;
            case "PowerShellFile":
            case "ShellFile":
                FilePathPanel.Visibility = Visibility.Visible;
                break;
            case "FileUpload":
                FileUploadPanel.Visibility = Visibility.Visible;
                break;
            case "WindowsRestart":
                RestartPanel.Visibility = Visibility.Visible;
                break;
            case "Custom":
                CustomPanel.Visibility = Visibility.Visible;
                break;
        }
    }

    private void ContentBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab && sender is TextBox textBox)
        {
            e.Handled = true;
            int selectionStart = textBox.SelectionStart;
            string text = textBox.Text;
            textBox.Text = text.Insert(selectionStart, "\t");
            textBox.SelectionStart = selectionStart + 1;
        }
    }

    private string GetContentForType(string typeTag)
    {
        switch (typeTag)
        {
            case "PowerShell":
            case "Shell":
                return ContentBox.Text;
            case "PowerShellFile":
            case "ShellFile":
                return FilePathBox.Text;
            case "FileUpload":
                return $"{UploadSourceBox.Text} => {UploadDestinationBox.Text}";
            case "Custom":
                return CustomContentBox.Text;
            case "WindowsRestart":
                return string.Empty;
            default:
                return ContentBox.Text;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        BuildStepType stepType = BuildStepType.PowerShell;
        string typeTag = "PowerShell";
        if (TypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            typeTag = tag;
            stepType = tag switch
            {
                "Shell" => BuildStepType.Shell,
                "PowerShellFile" => BuildStepType.PowerShellFile,
                "ShellFile" => BuildStepType.ShellFile,
                "FileUpload" => BuildStepType.FileUpload,
                "WindowsRestart" => BuildStepType.WindowsRestart,
                "Custom" => BuildStepType.Custom,
                _ => BuildStepType.PowerShell,
            };
        }

        int timeout = 300;
        if (int.TryParse(TimeoutBox.Text, out int parsed))
        {
            timeout = parsed;
        }

        List<string> tags = TagsBox
            .Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        string content = GetContentForType(typeTag);

        BuildStepLibraryEntry step = new BuildStepLibraryEntry
        {
            Id = _editingStepId ?? Guid.NewGuid().ToString(),
            Name = NameBox.Text,
            Description = DescriptionBox.Text,
            StepType = stepType,
            Content = content,
            DefaultTimeoutSeconds = timeout,
            Tags = tags,
        };

        if (_editingStepId is not null)
        {
            await App.ApiClient.UpdateStepAsync(_editingStepId, step);
        }
        else
        {
            await App.ApiClient.CreateStepAsync(step);
        }

        Frame? frame = this.Frame;
        if (frame != null && frame.CanGoBack)
        {
            frame.GoBack();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Frame? frame = this.Frame;
        if (frame != null && frame.CanGoBack)
        {
            frame.GoBack();
        }
    }
}
