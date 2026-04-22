using System.Net.Http;
using System.Text.Json;
using ForgeBoard.Contracts.Models;
using ForgeBoard.ViewModels;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace ForgeBoard.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly Serilog.ILogger _logger;
    private HubConnection? _hubConnection;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver =
            new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    public ApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient();
        _http.BaseAddress = new Uri(_baseUrl);
        _logger = Log.ForContext<ApiClient>();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            string message = !string.IsNullOrWhiteSpace(body)
                ? body
                : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            throw new HttpRequestException(message);
        }
    }

    private async Task<T> GetJsonAsync<T>(string url)
        where T : new()
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning("API request failed for {Url}: {Message}", url, ex.Message);
            return new T();
        }
        await EnsureSuccessAsync(response);
        string json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith('<'))
        {
            _logger.Warning("API returned non-JSON response for {Url}", url);
            return new T();
        }
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
    }

    private async Task<T?> GetJsonNullableAsync<T>(string url)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning("API request failed for {Url}: {Message}", url, ex.Message);
            return default;
        }
        await EnsureSuccessAsync(response);
        string json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith('<'))
        {
            _logger.Warning("API returned non-JSON response for {Url}", url);
            return default;
        }
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private async Task<T?> PostJsonAsync<T>(string url, object? body = null)
    {
        HttpResponseMessage response;
        if (body is not null)
        {
            string requestJson = JsonSerializer.Serialize(body, JsonOptions);
            StringContent content = new StringContent(
                requestJson,
                System.Text.Encoding.UTF8,
                "application/json"
            );
            response = await _http.PostAsync(url, content);
        }
        else
        {
            response = await _http.PostAsync(url, null);
        }
        await EnsureSuccessAsync(response);
        string responseJson = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseJson))
            return default;
        return JsonSerializer.Deserialize<T>(responseJson, JsonOptions);
    }

    private async Task PostAsync(string url, object? body = null)
    {
        HttpResponseMessage response;
        if (body is not null)
        {
            string requestJson = JsonSerializer.Serialize(body, JsonOptions);
            StringContent content = new StringContent(
                requestJson,
                System.Text.Encoding.UTF8,
                "application/json"
            );
            response = await _http.PostAsync(url, content);
        }
        else
        {
            response = await _http.PostAsync(url, null);
        }
        await EnsureSuccessAsync(response);
    }

    private async Task<T?> PutJsonAsync<T>(string url, object body)
    {
        string requestJson = JsonSerializer.Serialize(body, JsonOptions);
        StringContent content = new StringContent(
            requestJson,
            System.Text.Encoding.UTF8,
            "application/json"
        );
        HttpResponseMessage response = await _http.PutAsync(url, content);
        await EnsureSuccessAsync(response);
        string responseJson = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseJson))
            return default;
        return JsonSerializer.Deserialize<T>(responseJson, JsonOptions);
    }

    private async Task PutAsync(string url, object body)
    {
        string requestJson = JsonSerializer.Serialize(body, JsonOptions);
        StringContent content = new StringContent(
            requestJson,
            System.Text.Encoding.UTF8,
            "application/json"
        );
        HttpResponseMessage response = await _http.PutAsync(url, content);
        await EnsureSuccessAsync(response);
    }

    public async Task<List<BuildDefinition>> GetBuildDefinitionsAsync()
    {
        return await GetJsonAsync<List<BuildDefinition>>("api/builds/definitions");
    }

    public async Task<BuildDefinition> CreateBuildDefinitionAsync(BuildDefinition def)
    {
        _logger.Information("Creating build definition {Name}", def.Name);
        BuildDefinition? result = await PostJsonAsync<BuildDefinition>(
            "api/builds/definitions",
            def
        );
        _logger.Information("Created build definition {Id}", result!.Id);
        return result;
    }

    public async Task<BuildDefinition> UpdateBuildDefinitionAsync(
        string id,
        BuildDefinition definition
    )
    {
        BuildDefinition? result = await PutJsonAsync<BuildDefinition>(
            $"api/builds/definitions/{id}",
            definition
        );
        return result!;
    }

    public async Task<BuildExecution> StartBuildAsync(string definitionId)
    {
        _logger.Information("Starting build for definition {DefinitionId}", definitionId);
        BuildExecution? result = await PostJsonAsync<BuildExecution>(
            $"api/builds/executions/{definitionId}/start"
        );
        _logger.Information("Build execution {ExecutionId} started", result!.Id);
        return result;
    }

    public async Task<BuildDefinition> GetBuildDefinitionAsync(string id)
    {
        BuildDefinition? result = await GetJsonNullableAsync<BuildDefinition>(
            $"api/builds/definitions/{id}"
        );
        return result!;
    }

    public async Task DeleteBuildDefinitionAsync(string id)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"api/builds/definitions/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task CancelBuildAsync(string executionId)
    {
        await PostAsync($"api/builds/executions/{executionId}/cancel");
    }

    public async Task<List<BuildExecution>> GetBuildHistoryAsync()
    {
        return await GetJsonAsync<List<BuildExecution>>("api/builds/executions");
    }

    public async Task<BuildExecution> GetBuildExecutionAsync(string id)
    {
        BuildExecution? result = await GetJsonNullableAsync<BuildExecution>(
            $"api/builds/executions/{id}"
        );
        return result!;
    }

    public async Task<BuildPreviewResult> PreviewBuildAsync(BuildDefinition definition)
    {
        BuildPreviewResult? result = await PostJsonAsync<BuildPreviewResult>(
            "api/builds/preview",
            definition
        );

        if (result is not null && !string.IsNullOrEmpty(result.Hcl))
        {
            return result;
        }

        return new BuildPreviewResult
        {
            Hcl = "Preview generation failed",
            Steps = new List<string>(),
        };
    }

    public async Task<List<BaseImage>> GetBaseImagesAsync()
    {
        return await GetJsonAsync<List<BaseImage>>("api/images/base");
    }

    public async Task<List<BaseImage>> GetAllImagesAsync()
    {
        return await GetJsonAsync<List<BaseImage>>("api/images/all");
    }

    public async Task<List<ImageArtifact>> GetArtifactsAsync()
    {
        return await GetJsonAsync<List<ImageArtifact>>("api/images/artifacts");
    }

    public async Task<BaseImage> CreateBaseImageAsync(BaseImage image)
    {
        BaseImage? result = await PostJsonAsync<BaseImage>("api/images/base", image);
        return result!;
    }

    public async Task DeleteBaseImageAsync(string id)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"api/images/base/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteArtifactAsync(string id)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"api/images/artifacts/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<DiskUsageInfo> GetDiskUsageAsync()
    {
        DiskUsageInfo? result = await GetJsonNullableAsync<DiskUsageInfo>("api/images/disk-usage");
        return result!;
    }

    public async Task<BaseImage> ImportBaseImageAsync(string feedId, string remotePath)
    {
        BaseImage? result = await PostJsonAsync<BaseImage>(
            "api/images/base/import",
            new { FeedId = feedId, RemotePath = remotePath }
        );
        return result!;
    }

    public async Task<bool> ValidatePathAsync(string path)
    {
        try
        {
            await PostAsync("api/validation/path", new { Path = path });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task LaunchVmAsync(string artifactId, string? vmName = null)
    {
        VmLaunchRequest request = new VmLaunchRequest { VmName = vmName };
        await PostAsync($"api/images/artifacts/{artifactId}/launch-vm", request);
    }

    public async Task<VmLaunchProgress> GetLaunchProgressAsync(string artifactId)
    {
        VmLaunchProgress? result = await GetJsonNullableAsync<VmLaunchProgress>(
            $"api/images/artifacts/{artifactId}/launch-progress"
        );
        return result ?? new VmLaunchProgress { Status = "Unknown", IsComplete = true };
    }

    public async Task<Dictionary<string, VmLaunchProgress>> GetActiveLaunchesAsync()
    {
        return await GetJsonAsync<Dictionary<string, VmLaunchProgress>>(
            "api/images/artifacts/active-launches"
        );
    }

    public async Task DismissLaunchAsync(string artifactId)
    {
        await PostAsync($"api/images/artifacts/{artifactId}/dismiss-launch");
    }

    public async Task PromoteArtifactAsync(string id)
    {
        await PostAsync($"api/images/artifacts/{id}/promote");
    }

    public async Task<Feed> GetFeedAsync(string id)
    {
        Feed? result = await GetJsonNullableAsync<Feed>($"api/feeds/{id}");
        return result!;
    }

    public async Task<Feed> UpdateFeedAsync(string id, Feed feed)
    {
        Feed? result = await PutJsonAsync<Feed>($"api/feeds/{id}", feed);
        return result!;
    }

    public async Task<List<Feed>> GetFeedsAsync()
    {
        return await GetJsonAsync<List<Feed>>("api/feeds");
    }

    public async Task<Feed> CreateFeedAsync(Feed feed)
    {
        Feed? result = await PostJsonAsync<Feed>("api/feeds", feed);
        return result!;
    }

    public async Task DeleteFeedAsync(string id)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"api/feeds/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> TestFeedConnectivityAsync(string id)
    {
        try
        {
            await PostAsync($"api/feeds/{id}/test");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<FeedImage>> BrowseFeedAsync(string feedId)
    {
        return await GetJsonAsync<List<FeedImage>>($"api/feeds/{feedId}/browse");
    }

    public async Task<BuildStepLibraryEntry> GetStepAsync(string id)
    {
        BuildStepLibraryEntry? result = await GetJsonNullableAsync<BuildStepLibraryEntry>(
            $"api/steps/{id}"
        );
        return result!;
    }

    public async Task<BuildStepLibraryEntry> UpdateStepAsync(string id, BuildStepLibraryEntry step)
    {
        BuildStepLibraryEntry? result = await PutJsonAsync<BuildStepLibraryEntry>(
            $"api/steps/{id}",
            step
        );
        return result!;
    }

    public async Task<List<BuildStepLibraryEntry>> GetStepLibraryAsync()
    {
        return await GetJsonAsync<List<BuildStepLibraryEntry>>("api/steps");
    }

    public async Task<BuildStepLibraryEntry> CreateStepAsync(BuildStepLibraryEntry step)
    {
        BuildStepLibraryEntry? result = await PostJsonAsync<BuildStepLibraryEntry>(
            "api/steps",
            step
        );
        return result!;
    }

    public async Task<List<BuildStepLibraryEntry>> ExportStepsAsync()
    {
        return await GetJsonAsync<List<BuildStepLibraryEntry>>("api/steps/export");
    }

    public async Task<BuildStepLibraryEntry> ExportStepAsync(string id)
    {
        BuildStepLibraryEntry? result = await GetJsonNullableAsync<BuildStepLibraryEntry>(
            $"api/steps/{id}/export"
        );
        return result!;
    }

    public async Task<List<BuildStepLibraryEntry>> ImportStepsAsync(
        List<BuildStepLibraryEntry> entries
    )
    {
        List<BuildStepLibraryEntry>? result = await PostJsonAsync<List<BuildStepLibraryEntry>>(
            "api/steps/import",
            entries
        );
        return result ?? new List<BuildStepLibraryEntry>();
    }

    public async Task<BuildStepLibraryEntry> DuplicateStepAsync(string id)
    {
        BuildStepLibraryEntry? result = await PostJsonAsync<BuildStepLibraryEntry>(
            $"api/steps/{id}/duplicate"
        );
        return result!;
    }

    public async Task DeleteStepAsync(string id)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"api/steps/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<BuildLogEntry>> GetBuildLogsAsync(string executionId)
    {
        return await GetJsonAsync<List<BuildLogEntry>>($"api/builds/executions/{executionId}/logs");
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        AppSettings? result = await GetJsonNullableAsync<AppSettings>("api/settings");
        return result!;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await PutAsync("api/settings", settings);
    }

    public async Task<StoragePaths> GetStoragePathsAsync()
    {
        StoragePaths? result = await GetJsonNullableAsync<StoragePaths>("api/settings/storage");
        return result ?? new StoragePaths();
    }

    public async Task UpdateStoragePathsAsync(StoragePathsUpdateRequest request)
    {
        await PutAsync("api/settings/storage", request);
    }

    public async Task RestartApiAsync()
    {
        await PostAsync("api/settings/restart");
    }

    public async Task<string?> DetectPackerAsync()
    {
        HttpResponseMessage response = await _http.GetAsync("api/settings/detect-packer");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        string result = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(result) ? null : result.Trim('"');
    }

    public async Task<string> GetPackerVersionAsync(string packerPath)
    {
        HttpResponseMessage response = await _http.GetAsync(
            $"api/settings/packer-version?path={Uri.EscapeDataString(packerPath)}"
        );
        if (!response.IsSuccessStatusCode)
        {
            return "Validation failed";
        }
        string result = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(result) ? "Unknown" : result.Trim('"');
    }

    public async Task PublishArtifactAsync(string artifactId, PublishRequest request)
    {
        await PostAsync($"api/images/artifacts/{artifactId}/publish", request);
    }

    public async Task<PublishProgress> GetPublishProgressAsync(string artifactId)
    {
        PublishProgress? result = await GetJsonNullableAsync<PublishProgress>(
            $"api/images/artifacts/{artifactId}/publish-progress"
        );
        return result ?? new PublishProgress { Status = "Unknown", IsComplete = true };
    }

    public async Task<Dictionary<string, PublishProgress>> GetActivePublishesAsync()
    {
        return await GetJsonAsync<Dictionary<string, PublishProgress>>(
            "api/images/artifacts/active-publishes"
        );
    }

    public async Task CancelPublishAsync(string artifactId)
    {
        await PostAsync($"api/images/artifacts/{artifactId}/cancel-publish");
    }

    public async Task DismissPublishAsync(string artifactId)
    {
        await PostAsync($"api/images/artifacts/{artifactId}/dismiss-publish");
    }

    public async Task<List<string>> GetFeedRepositoriesAsync(string feedId)
    {
        return await GetJsonAsync<List<string>>($"api/images/feeds/{feedId}/repositories");
    }

    public async Task<BuildReadinessResult> CheckBuildReadinessAsync(string definitionId)
    {
        BuildReadinessResult? result = await PostJsonAsync<BuildReadinessResult>(
            $"api/builds/definitions/{definitionId}/check-readiness"
        );
        return result!;
    }

    public async Task<List<AvailableBuilder>> GetAvailableBuildersAsync()
    {
        return await GetJsonAsync<List<AvailableBuilder>>("api/validation/available-builders");
    }

    public async Task ConnectToLogStreamAsync(
        string executionId,
        Action<BuildLogEntry> onLogReceived,
        Action<string, string>? onStatusChanged = null
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(onLogReceived);

        await DisconnectLogStreamAsync();

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hubs/buildlog")
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<BuildLogEntry>("BuildLogReceived", onLogReceived);

        _hubConnection.On<string, string>(
            "BuildStatusChanged",
            (executionId, status) =>
            {
                onStatusChanged?.Invoke(executionId, status);
            }
        );

        await _hubConnection.StartAsync();
        await _hubConnection.InvokeAsync("JoinBuild", executionId);
        _logger.Information("Connected to log stream for execution {ExecutionId}", executionId);
    }

    public async Task DisconnectLogStreamAsync()
    {
        if (_hubConnection is not null)
        {
            try
            {
                if (_hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.StopAsync();
                }
            }
            catch (Exception) { }
            finally
            {
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }
    }
}
