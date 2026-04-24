using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services;

public sealed class VmLauncher
{
    private readonly ForgeBoardDatabase _db;
    private readonly ILogger<VmLauncher> _logger;
    private readonly HttpClient _httpClient;

    private static readonly ConcurrentDictionary<string, VmLaunchProgress> ActiveLaunches =
        new ConcurrentDictionary<string, VmLaunchProgress>();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public VmLauncher(ForgeBoardDatabase db, ILogger<VmLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    public VmLaunchProgress? GetProgress(string artifactId)
    {
        ActiveLaunches.TryGetValue(artifactId, out VmLaunchProgress? progress);
        return progress;
    }

    public Dictionary<string, VmLaunchProgress> GetAllLaunches()
    {
        return new Dictionary<string, VmLaunchProgress>(ActiveLaunches);
    }

    public void DismissLaunch(string artifactId)
    {
        ActiveLaunches.TryRemove(artifactId, out _);
    }

    public void StartLaunch(string artifactId, VmLaunchRequest? request)
    {
        ImageArtifact? artifact = _db.ImageArtifacts.FindById(artifactId);
        if (artifact is null)
        {
            throw new InvalidOperationException($"Artifact {artifactId} not found");
        }

        if (!File.Exists(artifact.FilePath))
        {
            throw new InvalidOperationException($"Artifact file not found: {artifact.FilePath}");
        }

        AppSettings? settings = _db.AppSettings.FindById(KnownIds.DefaultSettings);
        string agentUrl = (settings?.VmManagerAgentUrl ?? "http://localhost:18275").TrimEnd('/');

        string vmName = !string.IsNullOrWhiteSpace(request?.VmName)
            ? request.VmName.Trim()
            : GenerateVmName(artifact.Name);

        int memoryMb = request?.MemoryMb > 0 ? request.MemoryMb : 4096;
        int cpuCount = request?.CpuCount > 0 ? request.CpuCount : 4;

        VmLaunchProgress progress = new VmLaunchProgress
        {
            Status = "Starting...",
            VmName = vmName,
        };
        ActiveLaunches[artifactId] = progress;

        _ = Task.Run(async () =>
        {
            try
            {
                progress.Status = "Creating VM via VmManager...";

                string artifactDir = Path.GetDirectoryName(artifact.FilePath)!;

                Dictionary<string, object?> body = new Dictionary<string, object?>
                {
                    ["extractedFolder"] = artifactDir,
                    ["name"] = vmName,
                    ["memoryMb"] = memoryMb,
                    ["cpuCount"] = cpuCount,
                    ["origin"] = new Dictionary<string, string?>
                    {
                        ["imageId"] = artifact.BuildDefinitionId,
                        ["imageName"] = artifact.Name,
                        ["version"] = artifact.Version,
                    },
                };

                if (request?.Networks is { Count: > 0 })
                {
                    body["networks"] = request
                        .Networks.Select(adapter => new Dictionary<string, object?>
                        {
                            ["networkId"] = adapter.NetworkId,
                            ["staticIp"] = adapter.StaticIp,
                            ["gateway"] = adapter.Gateway,
                            ["dnsServers"] = adapter.DnsServers,
                            ["macAddress"] = adapter.MacAddress,
                            ["vlanId"] = adapter.VlanId,
                        })
                        .ToList();
                }

                string json = JsonSerializer.Serialize(body, JsonOptions);
                StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(
                    $"{agentUrl}/api/catalog/create-vm",
                    content
                );

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException(
                        $"VmManager returned {(int)response.StatusCode}: {errorBody}"
                    );
                }

                progress.Status = $"VM '{vmName}' created";
                progress.IsComplete = true;
                progress.IsSuccess = true;
                _logger.LogInformation("VM '{VmName}' creation started via VmManager", vmName);
            }
            catch (HttpRequestException ex)
            {
                progress.Status = "VmManager Agent not reachable";
                progress.Error =
                    $"Could not connect to VmManager Agent at {agentUrl}: {ex.Message}";
                progress.IsComplete = true;
                _logger.LogError(ex, "VmManager Agent not reachable at {Url}", agentUrl);
            }
            catch (Exception ex)
            {
                progress.Status = "Launch failed";
                progress.Error = ex.Message;
                progress.IsComplete = true;
                _logger.LogError(ex, "Failed to launch VM from artifact {ArtifactId}", artifactId);
            }
        });
    }

    private static string GenerateVmName(string artifactName)
    {
        string safeName = System
            .Text.RegularExpressions.Regex.Replace(artifactName, @"[^a-zA-Z0-9\-]", "-")
            .Trim('-');
        if (safeName.Length > 30)
        {
            safeName = safeName[..30];
        }
        return $"FB-{safeName}";
    }
}
