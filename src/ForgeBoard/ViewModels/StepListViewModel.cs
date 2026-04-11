using System.Collections.ObjectModel;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;

namespace ForgeBoard.ViewModels;

public partial class StepListViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<BuildStepLibraryEntry> Steps { get; } =
        new ObservableCollection<BuildStepLibraryEntry>();

    [ObservableProperty]
    private List<string> _categories = new List<string>();

    [ObservableProperty]
    private BuildStepLibraryEntry? _selectedStep;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public StepListViewModel(ApiClient api)
    {
        _api = api;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            List<BuildStepLibraryEntry> steps = await _api.GetStepLibraryAsync();
            Steps.Clear();
            foreach (BuildStepLibraryEntry step in steps)
            {
                Steps.Add(step);
            }
            Categories = steps.SelectMany(s => s.Tags).Distinct().OrderBy(c => c).ToList();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(string id)
    {
        try
        {
            await _api.DeleteStepAsync(id);
            BuildStepLibraryEntry? step = Steps.FirstOrDefault(s => s.Id == id);
            if (step != null)
            {
                Steps.Remove(step);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DuplicateAsync(string id)
    {
        try
        {
            BuildStepLibraryEntry duplicate = await _api.DuplicateStepAsync(id);
            Steps.Add(duplicate);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    public event Action<string>? ExportReady;

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        try
        {
            List<BuildStepLibraryEntry> exported = await _api.ExportStepsAsync();
            string json = System.Text.Json.JsonSerializer.Serialize(
                exported,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );
            ExportReady?.Invoke(json);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportSingleAsync(string id)
    {
        try
        {
            BuildStepLibraryEntry exported = await _api.ExportStepAsync(id);
            List<BuildStepLibraryEntry> wrapped = new List<BuildStepLibraryEntry> { exported };
            string json = System.Text.Json.JsonSerializer.Serialize(
                wrapped,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );
            ExportReady?.Invoke(json);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    public async Task ImportFromJsonAsync(string json)
    {
        ErrorMessage = null;
        try
        {
            List<BuildStepLibraryEntry>? entries = System.Text.Json.JsonSerializer.Deserialize<
                List<BuildStepLibraryEntry>
            >(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (entries is null || entries.Count == 0)
            {
                ErrorMessage = "No valid steps found in the file.";
                return;
            }

            List<BuildStepLibraryEntry> imported = await _api.ImportStepsAsync(entries);
            foreach (BuildStepLibraryEntry step in imported)
            {
                Steps.Add(step);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            ErrorMessage = "Invalid JSON format. Expected an array of step library entries.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }
}
