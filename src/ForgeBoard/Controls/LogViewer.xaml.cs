using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Controls;

public sealed partial class LogViewer : UserControl
{
    public static readonly DependencyProperty LogEntriesProperty = DependencyProperty.Register(
        nameof(LogEntries),
        typeof(ObservableCollection<BuildLogEntry>),
        typeof(LogViewer),
        new PropertyMetadata(null, OnLogEntriesChanged)
    );

    public ObservableCollection<BuildLogEntry>? LogEntries
    {
        get => (ObservableCollection<BuildLogEntry>?)GetValue(LogEntriesProperty);
        set => SetValue(LogEntriesProperty, value);
    }

    private string _filter = string.Empty;
    private int _renderedCount;

    public LogViewer()
    {
        InitializeComponent();
    }

    private static void OnLogEntriesChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        LogViewer control = (LogViewer)d;

        if (e.OldValue is ObservableCollection<BuildLogEntry> oldCol)
        {
            oldCol.CollectionChanged -= control.OnCollectionChanged;
        }

        if (e.NewValue is ObservableCollection<BuildLogEntry> newCol)
        {
            newCol.CollectionChanged += control.OnCollectionChanged;
            control.RebuildFull(newCol);
        }
        else
        {
            control.LogTextBox.Text = string.Empty;
            control._renderedCount = 0;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is not ObservableCollection<BuildLogEntry> collection)
            return;

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _renderedCount = 0;
            LogTextBox.Text = string.Empty;
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Add && collection.Count > _renderedCount)
        {
            AppendNewEntries(collection);
        }
    }

    private void RebuildFull(ObservableCollection<BuildLogEntry> source)
    {
        StringBuilder sb = new StringBuilder();
        foreach (BuildLogEntry entry in source)
        {
            if (Matches(entry))
            {
                sb.AppendLine($"[{entry.TimestampDisplay}] {entry.Message}");
            }
        }
        LogTextBox.Text = sb.ToString();
        _renderedCount = source.Count;
        ScrollToEnd();
    }

    private void AppendNewEntries(ObservableCollection<BuildLogEntry> source)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = _renderedCount; i < source.Count; i++)
        {
            BuildLogEntry entry = source[i];
            if (Matches(entry))
            {
                sb.AppendLine($"[{entry.TimestampDisplay}] {entry.Message}");
            }
        }

        if (sb.Length > 0)
        {
            LogTextBox.Text += sb.ToString();
            _renderedCount = source.Count;
            ScrollToEnd();
        }
    }

    private void ScrollToEnd()
    {
        if (!string.IsNullOrEmpty(LogTextBox.Text))
        {
            LogTextBox.SelectionStart = LogTextBox.Text.Length;
        }
    }

    private bool Matches(BuildLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_filter))
            return true;
        return entry.Message.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text ?? string.Empty;

        if (LogEntries is not null)
        {
            RebuildFull(LogEntries);
        }
    }
}
