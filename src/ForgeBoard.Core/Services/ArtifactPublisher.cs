using System.Collections.Concurrent;
using System.Text.Json;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using ForgeBoard.Core.Services.PostProcessors;
using ForgeBoard.Core.Services.Sources;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services;

public sealed class ArtifactPublisher
{
    private readonly ForgeBoardDatabase _db;
    private readonly IAppPaths _appPaths;
    private readonly IEnumerable<IFeedAdapter> _adapters;
    private readonly IEnumerable<IPostProcessor> _postProcessors;
    private readonly IPackerTemplateGenerator _templateGenerator;
    private readonly ILogger<ArtifactPublisher> _logger;

    private static readonly ConcurrentDictionary<string, PublishProgress> ActivePublishes =
        new ConcurrentDictionary<string, PublishProgress>();
    private static readonly ConcurrentDictionary<
        string,
        CancellationTokenSource
    > PublishCancellations = new ConcurrentDictionary<string, CancellationTokenSource>();

    public ArtifactPublisher(
        ForgeBoardDatabase db,
        IAppPaths appPaths,
        IEnumerable<IFeedAdapter> adapters,
        IEnumerable<IPostProcessor> postProcessors,
        IPackerTemplateGenerator templateGenerator,
        ILogger<ArtifactPublisher> logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(postProcessors);
        ArgumentNullException.ThrowIfNull(templateGenerator);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _appPaths = appPaths;
        _adapters = adapters;
        _postProcessors = postProcessors;
        _templateGenerator = templateGenerator;
        _logger = logger;
    }

    public PublishProgress? GetProgress(string artifactId)
    {
        ActivePublishes.TryGetValue(artifactId, out PublishProgress? progress);
        return progress;
    }

    public Dictionary<string, PublishProgress> GetAllActivePublishes()
    {
        return ActivePublishes
            .Where(p => !p.Value.IsComplete)
            .ToDictionary(p => p.Key, p => p.Value);
    }

    public Dictionary<string, PublishProgress> GetAllPublishes()
    {
        return new Dictionary<string, PublishProgress>(ActivePublishes);
    }

    public void CancelPublish(string artifactId)
    {
        if (PublishCancellations.TryGetValue(artifactId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            _logger.LogInformation("Cancel requested for publish {ArtifactId}", artifactId);
        }
        if (ActivePublishes.TryGetValue(artifactId, out PublishProgress? progress))
        {
            progress.Status = "Cancelling...";
        }
    }

    public void DismissPublish(string artifactId)
    {
        ActivePublishes.TryRemove(artifactId, out _);
    }

    public async Task PublishAsync(
        string artifactId,
        PublishRequest request,
        CancellationToken cancellationToken
    )
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

        Feed? feed = _db.Feeds.FindById(request.FeedId);
        if (feed is null)
        {
            throw new InvalidOperationException($"Feed {request.FeedId} not found");
        }

        if (!string.IsNullOrEmpty(request.Repository))
        {
            feed.Repository = request.Repository;
        }

        IFeedAdapter? adapter = _adapters.FirstOrDefault(a => a.SourceType == feed.SourceType);
        if (adapter is null)
        {
            throw new InvalidOperationException(
                $"No adapter found for feed type {feed.SourceType}"
            );
        }

        PublishProgress progress = new PublishProgress { Status = "Starting publish..." };
        ActivePublishes[artifactId] = progress;

        CancellationTokenSource publishCts = new CancellationTokenSource();
        PublishCancellations[artifactId] = publishCts;
        CancellationToken publishToken = publishCts.Token;

        try
        {
            cancellationToken = publishToken;
            string releaseNotes = request.ReleaseNotes ?? string.Empty;
            if (!string.IsNullOrEmpty(request.BuildSummary))
            {
                releaseNotes = string.IsNullOrEmpty(releaseNotes)
                    ? request.BuildSummary
                    : $"{releaseNotes}\n\n{request.BuildSummary}";
            }

            BuildDefinition? definition = _db.BuildDefinitions.FindById(artifact.BuildDefinitionId);
            string imageName = definition?.Name ?? artifact.Name;

            // Use a slugified build name as the image ID for clean Nexus paths
            string imageId = System
                .Text.RegularExpressions.Regex.Replace(
                    imageName.ToLowerInvariant(),
                    @"[^a-z0-9\-]",
                    "-"
                )
                .Trim('-');
            if (string.IsNullOrEmpty(imageId))
                imageId = artifact.BuildDefinitionId;

            string publishFilePath = artifact.FilePath;
            string? tempConvertDir = null;

            try
            {
                if (!string.IsNullOrEmpty(request.ConvertFormat) && request.ConvertFormat != "same")
                {
                    tempConvertDir = Path.Combine(
                        _appPaths.TempDirectory,
                        $"publish-{Guid.NewGuid():N}"
                    );
                    Directory.CreateDirectory(tempConvertDir);
                    publishFilePath = await ConvertArtifactAsync(
                        artifact.FilePath,
                        tempConvertDir,
                        request.ConvertFormat,
                        progress,
                        cancellationToken
                    );
                }

                if (feed.SourceType == FeedType.Nexus)
                {
                    await PublishToNexusAsync(
                        adapter,
                        feed,
                        publishFilePath,
                        artifact,
                        request,
                        imageName,
                        releaseNotes,
                        progress,
                        cancellationToken
                    );
                }
                else
                {
                    await PublishToFileSystemAsync(
                        adapter,
                        feed,
                        publishFilePath,
                        progress,
                        cancellationToken
                    );
                }
            }
            finally
            {
                if (tempConvertDir is not null)
                {
                    try
                    {
                        Directory.Delete(tempConvertDir, true);
                    }
                    catch { }
                }
            }

            progress.Status = "Published successfully";
            progress.PercentComplete = 100;
            progress.IsComplete = true;
            _logger.LogInformation(
                "Published artifact {ArtifactId} to feed {FeedId}",
                artifactId,
                request.FeedId
            );
        }
        catch (OperationCanceledException)
        {
            progress.Status = "Cancelled";
            progress.IsComplete = true;
            _logger.LogInformation("Publish cancelled for artifact {ArtifactId}", artifactId);
        }
        catch (Exception ex)
        {
            progress.Status = "Failed";
            progress.Error = ex.Message;
            progress.IsComplete = true;
            _logger.LogError(ex, "Failed to publish artifact {ArtifactId}", artifactId);
        }
        finally
        {
            PublishCancellations.TryRemove(artifactId, out CancellationTokenSource? removedCts);
            removedCts?.Dispose();
        }
    }

    private async Task<string> ConvertArtifactAsync(
        string sourcePath,
        string tempDir,
        string targetFormat,
        PublishProgress progress,
        CancellationToken cancellationToken
    )
    {
        if (targetFormat.Equals("box", StringComparison.OrdinalIgnoreCase))
        {
            // First convert to standalone VHDX (remove parent chain)
            IPostProcessor? convertVhd = _postProcessors.FirstOrDefault(p =>
                p.Name == "ConvertVhd"
            );
            string standaloneVhdx = sourcePath;

            string sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (sourceExt is ".vhdx" or ".vhd")
            {
                progress.Status = "Converting to standalone VHDX...";
                standaloneVhdx = Path.Combine(tempDir, "standalone.vhdx");
                if (convertVhd is not null)
                {
                    await convertVhd.ProcessAsync(
                        sourcePath,
                        standaloneVhdx,
                        msg =>
                        {
                            _logger.LogInformation(msg);
                            if (msg.Contains("completed", StringComparison.OrdinalIgnoreCase))
                                progress.Status = "VHD conversion completed";
                        },
                        cancellationToken
                    );
                }
                else
                {
                    File.Copy(sourcePath, standaloneVhdx, true);
                }
            }

            // Then create .box archive
            progress.Status = "Creating .box archive...";
            IPostProcessor? compressBox = _postProcessors.FirstOrDefault(p =>
                p.Name == "CompressBox"
            );
            string diskName = System
                .Text.RegularExpressions.Regex.Replace(
                    Path.GetFileNameWithoutExtension(sourcePath),
                    @"[^a-zA-Z0-9\-_]",
                    "-"
                )
                .Trim('-');
            if (string.IsNullOrEmpty(diskName))
                diskName = "disk";
            string boxPath = Path.Combine(tempDir, $"{diskName}.box");

            if (compressBox is not null)
            {
                await compressBox.ProcessAsync(
                    standaloneVhdx,
                    boxPath,
                    msg =>
                    {
                        _logger.LogInformation(msg);
                        if (msg.Contains("Compressing", StringComparison.OrdinalIgnoreCase))
                            progress.Status = "Compressing to .box archive...";
                        else if (msg.Contains("created", StringComparison.OrdinalIgnoreCase))
                            progress.Status = "Box archive created";
                    },
                    cancellationToken
                );
            }
            else
            {
                throw new InvalidOperationException("CompressBox post-processor not available");
            }

            string actualBox = File.Exists(boxPath) ? boxPath : boxPath + ".box";
            if (!File.Exists(actualBox))
            {
                throw new InvalidOperationException(
                    "Box conversion failed - output file not found"
                );
            }

            return actualBox;
        }

        if (targetFormat.Equals("vhdx", StringComparison.OrdinalIgnoreCase))
        {
            progress.Status = "Converting to standalone VHDX...";
            IPostProcessor? convertVhd = _postProcessors.FirstOrDefault(p =>
                p.Name == "ConvertVhd"
            );
            string vhdxName = System
                .Text.RegularExpressions.Regex.Replace(
                    Path.GetFileNameWithoutExtension(sourcePath),
                    @"[^a-zA-Z0-9\-_]",
                    "-"
                )
                .Trim('-');
            if (string.IsNullOrEmpty(vhdxName))
                vhdxName = "disk";
            string outputPath = Path.Combine(tempDir, $"{vhdxName}.vhdx");

            if (convertVhd is not null)
            {
                await convertVhd.ProcessAsync(
                    sourcePath,
                    outputPath,
                    msg =>
                    {
                        _logger.LogInformation(msg);
                        if (msg.Contains("completed", StringComparison.OrdinalIgnoreCase))
                            progress.Status = "VHD conversion completed";
                    },
                    cancellationToken
                );
            }
            else
            {
                File.Copy(sourcePath, outputPath, true);
            }

            return outputPath;
        }

        return sourcePath;
    }

    private async Task PublishToNexusAsync(
        IFeedAdapter adapter,
        Feed feed,
        string publishFilePath,
        ImageArtifact artifact,
        PublishRequest request,
        string imageName,
        string releaseNotes,
        PublishProgress progress,
        CancellationToken cancellationToken
    )
    {
        BuildDefinition? buildDef = _db.BuildDefinitions.FindById(artifact.BuildDefinitionId);
        string imageId = System
            .Text.RegularExpressions.Regex.Replace(
                imageName.ToLowerInvariant(),
                @"[^a-z0-9\-]",
                "-"
            )
            .Trim('-');
        if (string.IsNullOrEmpty(imageId))
            imageId = artifact.BuildDefinitionId;

        string version = request.Version;
        string fileName = Path.GetFileName(publishFilePath);
        long fileSize = new FileInfo(publishFilePath).Length;

        if (request.Features.Count == 0 && buildDef?.Tags.Count > 0)
        {
            request.Features = new List<string>(buildDef.Tags);
        }
        if (string.IsNullOrEmpty(request.BuildSummary) && buildDef is not null)
        {
            request.BuildSummary = buildDef.Description;
        }

        string fileSizeDisplay =
            fileSize > 1024 * 1024 * 1024
                ? $"{fileSize / (1024.0 * 1024.0 * 1024.0):F1} GB"
                : $"{fileSize / (1024.0 * 1024.0):F0} MB";
        progress.Status = $"Uploading {fileName} ({fileSizeDisplay})";
        string artifactPath = $"{imageId}/versions/{version}/{fileName}";

        DateTimeOffset uploadStart = DateTimeOffset.UtcNow;
        using (FileStream fileStream = File.OpenRead(publishFilePath))
        using (
            ProgressStream progressStream = new ProgressStream(
                fileStream,
                bytesRead =>
                {
                    double pct = fileSize > 0 ? Math.Min(95, bytesRead * 95.0 / fileSize) : 0;
                    progress.PercentComplete = pct;

                    double elapsed = (DateTimeOffset.UtcNow - uploadStart).TotalSeconds;
                    if (elapsed > 2)
                    {
                        double speedMb = (bytesRead / (1024.0 * 1024.0)) / elapsed;
                        long remaining = fileSize - bytesRead;
                        double etaSec = speedMb > 0 ? (remaining / (1024.0 * 1024.0)) / speedMb : 0;
                        string eta = TimeSpan.FromSeconds(etaSec).ToString(@"hh\:mm\:ss");
                        string uploaded =
                            bytesRead > 1024 * 1024 * 1024
                                ? $"{bytesRead / (1024.0 * 1024.0 * 1024.0):F1} GB"
                                : $"{bytesRead / (1024.0 * 1024.0):F0} MB";
                        progress.Status =
                            $"Uploading {fileName}: {uploaded} / {fileSizeDisplay} ({speedMb:F1} MB/s, ETA {eta})";
                    }
                }
            )
        )
        {
            await adapter.PushAsync(feed, artifactPath, progressStream, cancellationToken);
        }

        progress.Status = "Uploading manifest...";
        progress.PercentComplete = 96;

        object versionManifest = new
        {
            title = imageName,
            description = request.BuildSummary ?? string.Empty,
            version = version,
            releaseNotes = releaseNotes,
            features = request.Features,
            imageType = request.ImageType,
            releaseDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
            sizeBytes = fileSize,
        };

        string versionManifestJson = JsonSerializer.Serialize(
            versionManifest,
            new JsonSerializerOptions { WriteIndented = true }
        );
        string versionManifestPath = $"{imageId}/versions/{version}/manifest.json";

        using (
            MemoryStream manifestStream = new MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(versionManifestJson)
            )
        )
        {
            await adapter.PushAsync(feed, versionManifestPath, manifestStream, cancellationToken);
        }

        progress.Status = "Finalizing...";
        progress.PercentComplete = 98;

        object topManifest = new
        {
            title = imageName,
            description = request.BuildSummary ?? string.Empty,
            imageType = request.ImageType,
            features = request.Features,
        };

        string topManifestJson = JsonSerializer.Serialize(
            topManifest,
            new JsonSerializerOptions { WriteIndented = true }
        );
        string topManifestPath = $"{imageId}/manifest.json";

        using (
            MemoryStream topStream = new MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(topManifestJson)
            )
        )
        {
            await adapter.PushAsync(feed, topManifestPath, topStream, cancellationToken);
        }
    }

    private async Task PublishToFileSystemAsync(
        IFeedAdapter adapter,
        Feed feed,
        string publishFilePath,
        PublishProgress progress,
        CancellationToken cancellationToken
    )
    {
        string fileName = Path.GetFileName(publishFilePath);
        long fileSize = new FileInfo(publishFilePath).Length;
        string sizeDisplay =
            fileSize > 1024 * 1024 * 1024
                ? $"{fileSize / (1024.0 * 1024.0 * 1024.0):F1} GB"
                : $"{fileSize / (1024.0 * 1024.0):F0} MB";
        progress.Status = $"Copying {fileName} ({sizeDisplay})";

        using (FileStream fileStream = File.OpenRead(publishFilePath))
        {
            await adapter.PushAsync(feed, fileName, fileStream, cancellationToken);
        }

        progress.PercentComplete = 100;
    }
}
