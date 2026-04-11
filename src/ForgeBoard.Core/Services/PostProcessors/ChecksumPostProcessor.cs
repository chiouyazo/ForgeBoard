using System.Security.Cryptography;
using ForgeBoard.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.PostProcessors;

public sealed class ChecksumPostProcessor : IPostProcessor
{
    private readonly ILogger<ChecksumPostProcessor> _logger;

    public string Name => "Checksum";

    public ChecksumPostProcessor(ILogger<ChecksumPostProcessor> logger)
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
        ArgumentNullException.ThrowIfNull(log);

        string targetPath = File.Exists(outputPath) ? outputPath : inputPath;

        log($"Computing SHA256 checksum for {targetPath}");

        using (SHA256 sha256 = SHA256.Create())
        using (
            FileStream stream = new FileStream(
                targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920
            )
        )
        {
            byte[] hash = await sha256.ComputeHashAsync(stream, ct);
            string checksum = Convert.ToHexStringLower(hash);

            log($"SHA256: {checksum}");

            string checksumFilePath = targetPath + ".sha256";
            await File.WriteAllTextAsync(
                checksumFilePath,
                $"{checksum}  {Path.GetFileName(targetPath)}",
                ct
            );

            _logger.LogInformation("Checksum written to {Path}", checksumFilePath);
        }
    }
}
