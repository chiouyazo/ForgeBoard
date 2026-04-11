using System.Collections.ObjectModel;

namespace ForgeBoard.Controls;

public sealed partial class TagEditor : UserControl
{
    public static readonly DependencyProperty TagsProperty = DependencyProperty.Register(
        nameof(Tags),
        typeof(ObservableCollection<string>),
        typeof(TagEditor),
        new PropertyMetadata(null, OnTagsChanged)
    );

    public ObservableCollection<string>? Tags
    {
        get => (ObservableCollection<string>?)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public TagEditor()
    {
        this.InitializeComponent();
    }

    private static void OnTagsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        TagEditor editor = (TagEditor)d;
        if (e.NewValue is ObservableCollection<string> tags)
        {
            editor.TagList.ItemsSource = tags;
        }
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        AddCurrentTag();
    }

    private void NewTagBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            AddCurrentTag();
            e.Handled = true;
        }
    }

    private void AddCurrentTag()
    {
        string tag = NewTagBox.Text.Trim();
        if (string.IsNullOrEmpty(tag) || Tags == null)
            return;
        if (!Tags.Contains(tag))
        {
            Tags.Add(tag);
        }
        NewTagBox.Text = string.Empty;
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag && Tags != null)
        {
            Tags.Remove(tag);
        }
    }
}
