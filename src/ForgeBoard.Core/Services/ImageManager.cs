using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services;

public sealed class ImageManager : IImageManager
{
    private readonly ForgeBoardDatabase _db;
    private readonly IAppPaths _appPaths;
    private readonly ILogger<ImageManager> _logger;

    public ImageManager(ForgeBoardDatabase db, IAppPaths appPaths, ILogger<ImageManager> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _appPaths = appPaths;
        _logger = logger;
    }

    public Task<BaseImage> CreateBaseImageAsync(
        BaseImage image,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(image);

        if (string.IsNullOrEmpty(image.Id))
        {
            image.Id = Guid.NewGuid().ToString("N");
        }
        image.CreatedAt = DateTimeOffset.UtcNow;

        string? filePath = image.LocalCachePath ?? image.FileName;
        if (image.FileSizeBytes == 0 && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            image.FileSizeBytes = new FileInfo(filePath).Length;
            image.IsCached = true;
        }

        _db.BaseImages.Insert(image);

        _logger.LogInformation("Created base image {Id} ({Name})", image.Id, image.Name);
        return Task.FromResult(image);
    }

    public Task<BaseImage?> GetBaseImageAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(id);
        BaseImage? result = _db.BaseImages.FindById(id);
        return Task.FromResult<BaseImage?>(result);
    }

    public Task<List<BaseImage>> GetAllBaseImagesAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<BaseImage> result = _db.BaseImages.FindAll().ToList();
        return Task.FromResult(result);
    }

    public Task<BaseImage> UpdateBaseImageAsync(
        BaseImage image,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(image);

        _db.BaseImages.Update(image);

        _logger.LogInformation("Updated base image {Id}", image.Id);
        return Task.FromResult(image);
    }

    public Task DeleteBaseImageAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        bool hasRunningBuild = _db
            .BuildExecutions.Find(e =>
                e.Status == BuildStatus.Running || e.Status == BuildStatus.Preparing
            )
            .Any(e =>
            {
                BuildDefinition? def = _db.BuildDefinitions.FindById(e.BuildDefinitionId);
                return def is not null && def.BaseImageId == id;
            });

        if (hasRunningBuild)
        {
            throw new InvalidOperationException(
                $"Cannot delete base image {id} because it is used by a running build"
            );
        }

        BaseImage? image = _db.BaseImages.FindById(id);
        if (image is not null)
        {
            _db.BaseImages.Delete(id);
            _logger.LogInformation("Removed base image {Id} ({Name})", id, image.Name);
        }

        return Task.CompletedTask;
    }

    public Task<ImageArtifact?> GetArtifactAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(id);
        ImageArtifact? result = _db.ImageArtifacts.FindById(id);
        return Task.FromResult<ImageArtifact?>(result);
    }

    public Task<List<ImageArtifact>> GetAllArtifactsAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<ImageArtifact> result = _db.ImageArtifacts.FindAll().ToList();
        return Task.FromResult(result);
    }

    public Task DeleteArtifactAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        bool hasRunningBuild = _db
            .BuildExecutions.Find(e =>
                e.Status == BuildStatus.Running || e.Status == BuildStatus.Preparing
            )
            .Any(e => e.ArtifactId == id);

        if (hasRunningBuild)
        {
            throw new InvalidOperationException(
                $"Cannot delete artifact {id} because it is used by a running build"
            );
        }

        ImageArtifact? artifact = _db.ImageArtifacts.FindById(id);
        if (artifact is not null)
        {
            string? artifactDir = Path.GetDirectoryName(artifact.FilePath);
            if (artifactDir is not null && Directory.Exists(artifactDir))
            {
                try
                {
                    Directory.Delete(artifactDir, true);
                    _logger.LogInformation("Deleted artifact directory {Path}", artifactDir);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to delete artifact directory {Path}",
                        artifactDir
                    );
                }
            }
            _db.ImageArtifacts.Delete(id);
            _logger.LogInformation("Deleted artifact {Id}", id);
        }

        return Task.CompletedTask;
    }

    public Task<DiskUsageInfo> GetDiskUsageAsync(CancellationToken cancellationToken = default)
    {
        _appPaths.EnsureDirectoriesExist();

        DiskUsageInfo info = new DiskUsageInfo();

        info.CacheSizeBytes = GetDirectorySize(_appPaths.CacheDirectory);
        info.ArtifactSizeBytes = GetDirectorySize(_appPaths.ArtifactsDirectory);
        info.WorkingSizeBytes = GetDirectorySize(_appPaths.WorkingDirectory);
        info.TotalSizeBytes = info.CacheSizeBytes + info.ArtifactSizeBytes + info.WorkingSizeBytes;

        info.CachedImageCount = _db.BaseImages.Count(i => i.IsCached);
        info.ArtifactCount = _db.ImageArtifacts.Count();

        try
        {
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(_appPaths.DataDirectory) ?? "C");
            info.DriveTotalBytes = drive.TotalSize;
            info.DriveFreeBytes = drive.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read drive space information");
        }

        return Task.FromResult(info);
    }

    public Task<List<BaseImage>> GetAllMergedAsync(CancellationToken cancellationToken = default)
    {
        List<BaseImage> baseImages = _db.BaseImages.FindAll().ToList();

        List<ImageArtifact> artifacts = _db.ImageArtifacts.FindAll().ToList();
        foreach (ImageArtifact artifact in artifacts)
        {
            BaseImage promoted = new BaseImage
            {
                Id = $"{BaseImagePrefixes.Artifact}{artifact.Id}",
                Name = artifact.Name,
                Description = $"Build artifact from execution {artifact.BuildExecutionId}",
                FileName = Path.GetFileName(artifact.FilePath),
                ImageFormat = artifact.Format,
                Checksum = artifact.Checksum,
                FileSizeBytes = artifact.FileSizeBytes,
                Origin = ImageOrigin.Built,
                LocalCachePath = artifact.FilePath,
                IsCached = File.Exists(artifact.FilePath),
                CreatedAt = artifact.CreatedAt,
            };
            baseImages.Add(promoted);
        }

        List<BuildDefinition> definitions = _db.BuildDefinitions.FindAll().ToList();
        foreach (BuildDefinition definition in definitions)
        {
            BaseImage chainable = new BaseImage
            {
                Id = $"{BaseImagePrefixes.BuildChain}{definition.Id}",
                Name = $"[Build] {definition.Name}",
                Description =
                    $"Runs build '{definition.Name}' first, then uses its output as base image",
                FileName = "",
                ImageFormat = "chain",
                Origin = ImageOrigin.BuildChain,
                LinkedBuildDefinitionId = definition.Id,
                IsCached = false,
                CreatedAt = definition.CreatedAt,
            };
            baseImages.Add(chainable);
        }

        return Task.FromResult(baseImages);
    }

    public Task<BaseImage> PromoteArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(artifactId);

        ImageArtifact? artifact = _db.ImageArtifacts.FindById(artifactId);
        if (artifact is null)
        {
            throw new InvalidOperationException($"Artifact {artifactId} not found");
        }

        BaseImage image = new BaseImage
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = artifact.Name,
            Description = $"Promoted from build artifact {artifact.Id}",
            FileName = Path.GetFileName(artifact.FilePath),
            Checksum = artifact.Checksum,
            FileSizeBytes = artifact.FileSizeBytes,
            Origin = ImageOrigin.Built,
            LocalCachePath = artifact.FilePath,
            IsCached = File.Exists(artifact.FilePath),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.BaseImages.Insert(image);

        _logger.LogInformation(
            "Promoted artifact {ArtifactId} to base image {ImageId}",
            artifactId,
            image.Id
        );
        return Task.FromResult(image);
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        DirectoryInfo directory = new DirectoryInfo(path);
        return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
    }
}
