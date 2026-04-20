using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;

namespace ForgeBoard.ViewModels;

public partial class BuildWizardViewModel : ObservableObject
{
    private readonly ApiClient _api;

    private readonly ObservableCollection<BaseImageDisplayItem> _allBaseImages =
        new ObservableCollection<BaseImageDisplayItem>();
    private readonly ObservableCollection<BuildStepLibraryEntry> _allAvailableSteps =
        new ObservableCollection<BuildStepLibraryEntry>();

    public ObservableCollection<BaseImageDisplayItem> FilteredBaseImages { get; } =
        new ObservableCollection<BaseImageDisplayItem>();
    public ObservableCollection<BuildStepLibraryEntry> FilteredAvailableSteps { get; } =
        new ObservableCollection<BuildStepLibraryEntry>();
    public ObservableCollection<BuildStep> BuildSteps { get; } =
        new ObservableCollection<BuildStep>();
    public ObservableCollection<BuildStep> PackerSteps { get; } =
        new ObservableCollection<BuildStep>();
    public ObservableCollection<BuildStep> DirectSteps { get; } =
        new ObservableCollection<BuildStep>();

    [ObservableProperty]
    private bool _hasPackerSteps;

    [ObservableProperty]
    private bool _hasDirectSteps;

    public ObservableCollection<PackerBuilder> AvailableBuilders { get; } =
        new ObservableCollection<PackerBuilder>();
    public List<string> AvailableOutputFormats { get; } =
        new List<string> { "qcow2", "vhdx", "vmdk", "raw" };

    [ObservableProperty]
    private string? _builderWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    private int _currentStep;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIsoBaseImage))]
    private BaseImageDisplayItem? _selectedBaseImage;

    [ObservableProperty]
    private string _outputName = string.Empty;

    [ObservableProperty]
    private string _outputFormat = "qcow2";

    [ObservableProperty]
    private PackerBuilder _builder = PackerBuilder.Qemu;

    [ObservableProperty]
    private int _memoryMb = 4096;

    [ObservableProperty]
    private int _cpuCount = 2;

    [ObservableProperty]
    private long _diskSizeGb = 40;

    [ObservableProperty]
    private string _tags = string.Empty;

    [ObservableProperty]
    private string _postProcessors = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _version = "1.0";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string _imageSearchText = string.Empty;

    [ObservableProperty]
    private string _stepSearchText = string.Empty;

    [ObservableProperty]
    private BuildStep? _selectedStepForEdit;

    public event Action<string>? NavigateToBuildDetail;
    public event Action? NavigateToBuildList;
    public event Action<string, List<string>>? ShowPreviewDialog;

    [ObservableProperty]
    private string _pageTitle = "New Build";

    [ObservableProperty]
    private string? _editingDefinitionId;

    [ObservableProperty]
    private string _unattendPath = string.Empty;

    public bool IsIsoBaseImage
    {
        get
        {
            if (SelectedBaseImage is null)
            {
                return false;
            }
            return SelectedBaseImage.Format.Equals("ISO", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsFirstStep => CurrentStep == 0;
    public bool IsLastStep => CurrentStep == 3;
    public string NextButtonText => IsLastStep ? "Start Build" : "Next";
    public bool IsEditing => !string.IsNullOrEmpty(EditingDefinitionId);

    public BuildWizardViewModel(ApiClient api)
    {
        _api = api;
        BuildSteps.CollectionChanged += OnBuildStepsCollectionChanged;
    }

    private void OnBuildStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshGroupedSteps();
    }

    public void RefreshGroupedSteps()
    {
        PackerSteps.Clear();
        DirectSteps.Clear();
        foreach (BuildStep step in BuildSteps)
        {
            if (step.UsePacker)
            {
                PackerSteps.Add(step);
            }
            else
            {
                DirectSteps.Add(step);
            }
        }
        HasPackerSteps = PackerSteps.Count > 0;
        HasDirectSteps = DirectSteps.Count > 0;
    }

    partial void OnImageSearchTextChanged(string value)
    {
        ApplyImageFilter();
    }

    partial void OnStepSearchTextChanged(string value)
    {
        ApplyStepFilter();
    }

    private void ApplyImageFilter()
    {
        FilteredBaseImages.Clear();
        foreach (BaseImageDisplayItem image in _allBaseImages)
        {
            // Don't show the current build definition as a base image option (circular dependency)
            if (
                !string.IsNullOrEmpty(EditingDefinitionId)
                && image.Id == $"{BaseImagePrefixes.BuildChain}{EditingDefinitionId}"
            )
            {
                continue;
            }

            bool matches =
                string.IsNullOrWhiteSpace(ImageSearchText)
                || image.Name.Contains(ImageSearchText, StringComparison.OrdinalIgnoreCase)
                || image.FileName.Contains(ImageSearchText, StringComparison.OrdinalIgnoreCase)
                || image.Description.Contains(ImageSearchText, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                FilteredBaseImages.Add(image);
            }
        }
    }

    private void ApplyStepFilter()
    {
        FilteredAvailableSteps.Clear();
        foreach (BuildStepLibraryEntry step in _allAvailableSteps)
        {
            bool matches =
                string.IsNullOrWhiteSpace(StepSearchText)
                || step.Name.Contains(StepSearchText, StringComparison.OrdinalIgnoreCase)
                || (step.Description ?? string.Empty).Contains(
                    StepSearchText,
                    StringComparison.OrdinalIgnoreCase
                );

            if (matches)
            {
                FilteredAvailableSteps.Add(step);
            }
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await LoadBaseImagesAsync();
            await LoadStepLibraryAsync();
            await LoadAvailableBuildersAsync();
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

    private async Task LoadBaseImagesAsync()
    {
        List<BaseImage> images;
        try
        {
            images = await _api.GetAllImagesAsync();
        }
        catch
        {
            images = await _api.GetBaseImagesAsync();
        }

        _allBaseImages.Clear();
        foreach (BaseImage image in images)
        {
            string extension = string.Empty;
            if (!string.IsNullOrEmpty(image.FileName))
            {
                extension = System
                    .IO.Path.GetExtension(image.FileName)
                    .TrimStart('.')
                    .ToUpperInvariant();
            }
            if (string.IsNullOrEmpty(extension) && !string.IsNullOrEmpty(image.ImageFormat))
            {
                extension = image.ImageFormat.ToUpperInvariant();
            }

            string origin = image.Origin switch
            {
                ImageOrigin.Local => "Local",
                ImageOrigin.Imported => "Imported",
                ImageOrigin.Built => "Built",
                ImageOrigin.BuildChain => "BuildChain",
                _ => string.IsNullOrEmpty(image.SourceId) ? "Local" : "Imported",
            };

            string displayName = image.Name;

            _allBaseImages.Add(
                new BaseImageDisplayItem
                {
                    Id = image.Id,
                    Name = displayName,
                    FileName = image.FileName,
                    Description = image.Description,
                    Format = string.IsNullOrEmpty(extension) ? "Unknown" : extension,
                    Origin = origin,
                    FileSizeBytes = image.FileSizeBytes,
                    SourceImage = image,
                }
            );
        }
        ApplyImageFilter();
    }

    private async Task LoadStepLibraryAsync()
    {
        List<BuildStepLibraryEntry> steps = await _api.GetStepLibraryAsync();
        _allAvailableSteps.Clear();
        foreach (BuildStepLibraryEntry step in steps)
        {
            _allAvailableSteps.Add(step);
        }
        ApplyStepFilter();
    }

    private async Task LoadAvailableBuildersAsync()
    {
        AvailableBuilders.Clear();
        BuilderWarning = null;
        try
        {
            List<AvailableBuilder> detectedBuilders = await _api.GetAvailableBuildersAsync();
            foreach (AvailableBuilder detected in detectedBuilders)
            {
                if (detected.IsAvailable)
                {
                    AvailableBuilders.Add(detected.Builder);
                }
            }

            if (AvailableBuilders.Count == 0)
            {
                BuilderWarning = "No builders available. Install QEMU or enable Hyper-V.";
                foreach (PackerBuilder fallback in Enum.GetValues<PackerBuilder>())
                {
                    AvailableBuilders.Add(fallback);
                }
            }
            else if (AvailableBuilders.Count == 1)
            {
                Builder = AvailableBuilders[0];
            }
        }
        catch
        {
            foreach (PackerBuilder fallback in Enum.GetValues<PackerBuilder>())
            {
                AvailableBuilders.Add(fallback);
            }
        }
    }

    [RelayCommand]
    private void Next()
    {
        ValidationMessage = null;

        if (CurrentStep == 0 && SelectedBaseImage is null)
        {
            ValidationMessage = "Please select a base image before continuing.";
            Views.Shell.Current?.ShowWarning("Please select a base image before continuing.");
            return;
        }

        if (CurrentStep == 2 && string.IsNullOrWhiteSpace(OutputName))
        {
            ValidationMessage = "Please enter an output name before continuing.";
            Views.Shell.Current?.ShowWarning("Please enter an output name before continuing.");
            return;
        }

        if (CurrentStep < 3)
        {
            CurrentStep++;
            return;
        }

        if (IsLastStep)
        {
            StartBuildCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private void AddStepFromLibrary(string libraryEntryId)
    {
        BuildStepLibraryEntry? entry = _allAvailableSteps.FirstOrDefault(s =>
            s.Id == libraryEntryId
        );
        if (entry is null)
            return;

        BuildStep step = new BuildStep
        {
            Id = Guid.NewGuid().ToString(),
            Order = BuildSteps.Count,
            Name = entry.Name,
            StepType = entry.StepType,
            Content = entry.Content,
            TimeoutSeconds = entry.DefaultTimeoutSeconds,
            ExpectReboot = entry.ExpectReboot,
            UsePacker = entry.UsePacker,
            LibraryEntryId = entry.Id,
        };
        BuildSteps.Add(step);
    }

    [RelayCommand]
    private void AddCustomStep()
    {
        BuildStep step = new BuildStep
        {
            Id = Guid.NewGuid().ToString(),
            Order = BuildSteps.Count,
            Name = "Custom Step",
            StepType = BuildStepType.PowerShell,
        };
        BuildSteps.Add(step);
    }

    [RelayCommand]
    private void RemoveStep(string id)
    {
        BuildStep? step = BuildSteps.FirstOrDefault(s => s.Id == id);
        if (step is not null)
        {
            BuildSteps.Remove(step);
            for (int i = 0; i < BuildSteps.Count; i++)
            {
                BuildSteps[i].Order = i;
            }

            if (SelectedStepForEdit is not null && SelectedStepForEdit.Id == id)
            {
                SelectedStepForEdit = null;
            }
        }
    }

    [RelayCommand]
    private async Task SaveStepToLibraryAsync(string stepId)
    {
        BuildStep? step = BuildSteps.FirstOrDefault(s => s.Id == stepId);
        if (step is null)
            return;

        try
        {
            BuildStepLibraryEntry entry = new BuildStepLibraryEntry
            {
                Name = step.Name,
                Description = $"Saved from build step",
                StepType = step.StepType,
                Content = step.Content,
                DefaultTimeoutSeconds = step.TimeoutSeconds,
                ExpectReboot = step.ExpectReboot,
                UsePacker = step.UsePacker,
                Tags = new List<string>(),
            };

            BuildStepLibraryEntry created = await _api.CreateStepAsync(entry);
            _allAvailableSteps.Add(created);
            ApplyStepFilter();
            Views.Shell.Current?.ShowNotification($"Step '{step.Name}' saved to library.");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private void Validate()
    {
        if (SelectedBaseImage is null)
        {
            ValidationMessage = "Please select a base image.";
            return;
        }
        if (string.IsNullOrWhiteSpace(OutputName))
        {
            ValidationMessage = "Please enter an output name.";
            return;
        }
        if (BuildSteps.Count == 0)
        {
            ValidationMessage = "Please add at least one build step.";
            return;
        }
        ValidationMessage = "Validation passed.";
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (SelectedBaseImage is null)
        {
            ValidationMessage = "Please select a base image to preview.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            BuildDefinition definition = BuildCurrentDefinition();
            BuildPreviewResult preview = await _api.PreviewBuildAsync(definition);

            List<string> stepFlow = new List<string>();
            if (preview.Steps.Count > 0)
            {
                stepFlow.AddRange(preview.Steps);
            }
            else
            {
                stepFlow.Add($"Base image: {SelectedBaseImage.Name}");
                foreach (BuildStep step in BuildSteps)
                {
                    stepFlow.Add($"{step.Name} ({step.StepType})");
                }
                stepFlow.Add($"Output: {OutputFormat} via {Builder}");
            }

            ShowPreviewDialog?.Invoke(preview.Hcl, stepFlow);
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
    private async Task SaveDraftAsync()
    {
        if (SelectedBaseImage is null)
        {
            ValidationMessage = "Please select a base image.";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputName))
        {
            ValidationMessage = "Please enter an output name.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            BuildDefinition definition = BuildCurrentDefinition();
            if (IsEditing)
            {
                await _api.UpdateBuildDefinitionAsync(definition.Id, definition);
            }
            else
            {
                await _api.CreateBuildDefinitionAsync(definition);
            }
            NavigateToBuildList?.Invoke();
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
    private async Task StartBuildAsync()
    {
        if (SelectedBaseImage is null)
            return;

        if (string.IsNullOrWhiteSpace(OutputName))
        {
            ValidationMessage = "Please enter an output name.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            BuildDefinition definition = BuildCurrentDefinition();
            BuildDefinition saved;
            if (IsEditing)
            {
                saved = await _api.UpdateBuildDefinitionAsync(definition.Id, definition);
            }
            else
            {
                saved = await _api.CreateBuildDefinitionAsync(definition);
            }
            BuildExecution execution = await _api.StartBuildAsync(saved.Id);
            NavigateToBuildDetail?.Invoke(execution.Id);
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

    public async Task LoadDefinitionAsync(string definitionId)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            BuildDefinition definition = await _api.GetBuildDefinitionAsync(definitionId);
            EditingDefinitionId = definition.Id;
            PageTitle = $"Edit: {definition.Name}";

            OutputName = definition.Name;
            Description = definition.Description;
            Builder = definition.Builder;
            OutputFormat = definition.OutputFormat;
            Version = definition.Version;
            MemoryMb = definition.MemoryMb;
            CpuCount = definition.CpuCount;
            DiskSizeGb = definition.DiskSizeMb / 1024;
            Tags = string.Join(", ", definition.Tags);
            PostProcessors = string.Join(", ", definition.PostProcessors);
            UnattendPath = definition.UnattendPath ?? string.Empty;

            BaseImageDisplayItem? matchingImage = null;
            foreach (BaseImageDisplayItem image in _allBaseImages)
            {
                if (image.Id == definition.BaseImageId)
                {
                    matchingImage = image;
                    break;
                }
            }
            SelectedBaseImage = matchingImage;

            BuildSteps.Clear();
            foreach (BuildStep step in definition.Steps)
            {
                if (string.IsNullOrEmpty(step.Id))
                {
                    step.Id = Guid.NewGuid().ToString("N");
                }
                BuildSteps.Add(step);
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

    private BuildDefinition BuildCurrentDefinition()
    {
        List<string> tagList = Tags.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToList();

        List<string> postProcessorList = PostProcessors
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        BuildDefinition definition = new BuildDefinition
        {
            Id = EditingDefinitionId ?? Guid.NewGuid().ToString(),
            Name = OutputName,
            Description = Description,
            BaseImageId = SelectedBaseImage!.Id,
            Builder = Builder,
            OutputFormat = OutputFormat,
            Version = Version,
            MemoryMb = MemoryMb,
            CpuCount = CpuCount,
            DiskSizeMb = DiskSizeGb * 1024,
            UnattendPath = string.IsNullOrWhiteSpace(UnattendPath) ? null : UnattendPath,
            Steps = BuildSteps.ToList(),
            Tags = tagList,
            PostProcessors = postProcessorList,
        };

        return definition;
    }
}
