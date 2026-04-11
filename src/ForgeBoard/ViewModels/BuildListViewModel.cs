using System.Collections.ObjectModel;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;

namespace ForgeBoard.ViewModels;

public partial class BuildListViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<BuildDefinition> Definitions { get; } =
        new ObservableCollection<BuildDefinition>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public BuildListViewModel(ApiClient api)
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
            List<BuildDefinition> definitions = await _api.GetBuildDefinitionsAsync();
            Definitions.Clear();
            foreach (BuildDefinition definition in definitions)
            {
                Definitions.Add(definition);
            }
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

    public event Action<string, BuildReadinessResult>? ShowReadinessResult;
    public event Action<string>? NavigateToBuildDetail;

    [RelayCommand]
    private async Task RunBuildAsync(string definitionId)
    {
        try
        {
            BuildReadinessResult readiness = await _api.CheckBuildReadinessAsync(definitionId);
            if (!readiness.IsReady)
            {
                ShowReadinessResult?.Invoke(definitionId, readiness);
                return;
            }

            BuildExecution execution = await _api.StartBuildAsync(definitionId);
            NavigateToBuildDetail?.Invoke(execution.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(string definitionId)
    {
        try
        {
            await _api.DeleteBuildDefinitionAsync(definitionId);
            BuildDefinition? toRemove = Definitions.FirstOrDefault(d => d.Id == definitionId);
            if (toRemove is not null)
            {
                Definitions.Remove(toRemove);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task CloneAsync(string definitionId)
    {
        BuildDefinition? source = Definitions.FirstOrDefault(d => d.Id == definitionId);
        if (source is null)
            return;

        BuildDefinition clone = new BuildDefinition
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{source.Name} (Copy)",
            Description = source.Description,
            BaseImageId = source.BaseImageId,
            Builder = source.Builder,
            OutputFormat = source.OutputFormat,
            Version = source.Version,
            PackerRunnerConfigId = source.PackerRunnerConfigId,
            MemoryMb = source.MemoryMb,
            CpuCount = source.CpuCount,
            DiskSizeMb = source.DiskSizeMb,
            UnattendPath = source.UnattendPath,
            Steps = new List<BuildStep>(source.Steps),
            Tags = new List<string>(source.Tags),
            PostProcessors = new List<string>(source.PostProcessors),
        };

        try
        {
            BuildDefinition created = await _api.CreateBuildDefinitionAsync(clone);
            Definitions.Add(created);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }
}
