using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using ForgeBoard.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.PostProcessors;

public sealed class CompressBoxPostProcessor : IPostProcessor
{
    private readonly ILogger<CompressBoxPostProcessor> _logger;

    public string Name => "CompressBox";

    public CompressBoxPostProcessor(ILogger<CompressBoxPostProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task ProcessAsync(
        string inputPath,
        string outputPath,
        Action<string> log,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(log);

        log($"Creating .box archive from {Path.GetFileName(inputPath)}");

        string outputDir = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
        string tempDir = Path.Combine(outputDir, $"box-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string stagedDisk = Path.Combine(tempDir, "disk" + Path.GetExtension(inputPath));
            File.Copy(inputPath, stagedDisk, true);

            Dictionary<string, string> metadata = new Dictionary<string, string>
            {
                ["provider"] = "hyperv",
                ["format"] = Path.GetExtension(inputPath).TrimStart('.'),
                ["virtual_size"] = new FileInfo(inputPath).Length.ToString(),
            };

            string metadataPath = Path.Combine(tempDir, "metadata.json");
            string metadataJson = JsonSerializer.Serialize(
                metadata,
                new JsonSerializerOptions { WriteIndented = true }
            );
            await File.WriteAllTextAsync(metadataPath, metadataJson, ct);

            log("Compressing to tar.gz...");

            string boxPath = outputPath.EndsWith(".box", StringComparison.OrdinalIgnoreCase)
                ? outputPath
                : outputPath + ".box";

            using (
                FileStream boxStream = new FileStream(
                    boxPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            using (GZipStream gzipStream = new GZipStream(boxStream, CompressionLevel.Optimal))
            {
                await TarFile.CreateFromDirectoryAsync(tempDir, gzipStream, false, ct);
            }

            log($"Box archive created: {boxPath} ({new FileInfo(boxPath).Length} bytes)");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp directory {TempDir}", tempDir);
            }
        }
    }
}
