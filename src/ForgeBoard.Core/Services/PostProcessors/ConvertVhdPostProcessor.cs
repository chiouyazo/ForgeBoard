using System.Runtime.InteropServices;
using CliWrap;
using CliWrap.Buffered;
using ForgeBoard.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.PostProcessors;

public sealed class ConvertVhdPostProcessor : IPostProcessor
{
    private readonly ILogger<ConvertVhdPostProcessor> _logger;

    public string Name => "ConvertVhd";

    public ConvertVhdPostProcessor(ILogger<ConvertVhdPostProcessor> logger)
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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await RunPowerShellConvertAsync(inputPath, outputPath, log, ct);
        }
        else
        {
            await RunQemuConvertAsync(inputPath, outputPath, log, ct);
        }
    }

    private async Task RunPowerShellConvertAsync(
        string inputPath,
        string outputPath,
        Action<string> log,
        CancellationToken ct
    )
    {
        log($"Converting VHD via PowerShell: {inputPath} -> {outputPath}");

        string script =
            $"Convert-VHD -Path '{inputPath}' -DestinationPath '{outputPath}' -VHDType Dynamic";

        BufferedCommandResult result = await Cli.Wrap("powershell")
            .WithArguments(new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(ct);

        if (result.ExitCode != 0)
        {
            string error = result.StandardError.Trim();
            _logger.LogError(
                "Convert-VHD failed with exit code {ExitCode}: {Error}",
                result.ExitCode,
                error
            );
            throw new InvalidOperationException(
                $"Convert-VHD failed (exit code {result.ExitCode}): {error}"
            );
        }

        log("VHD conversion completed successfully");
    }

    private async Task RunQemuConvertAsync(
        string inputPath,
        string outputPath,
        Action<string> log,
        CancellationToken ct
    )
    {
        log($"Converting VHD via qemu-img: {inputPath} -> {outputPath}");

        BufferedCommandResult result = await Cli.Wrap("qemu-img")
            .WithArguments(new[] { "convert", "-f", "vpc", "-O", "vhdx", inputPath, outputPath })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(ct);

        if (result.ExitCode != 0)
        {
            string error = result.StandardError.Trim();
            _logger.LogError(
                "qemu-img convert failed with exit code {ExitCode}: {Error}",
                result.ExitCode,
                error
            );
            throw new InvalidOperationException(
                $"qemu-img convert failed (exit code {result.ExitCode}): {error}"
            );
        }

        log("VHD conversion completed successfully");
    }
}
