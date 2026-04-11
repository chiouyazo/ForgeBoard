using CliWrap;
using CliWrap.Buffered;

namespace ForgeBoard.Core.Services.Build;

public static class PowerShellRunner
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string script,
        CancellationToken ct
    )
    {
        BufferedCommandResult result = await Cli.Wrap("powershell")
            .WithArguments(new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(ct);

        return (result.ExitCode, result.StandardOutput, result.StandardError);
    }

    public static async Task<int> RunFireAndForgetAsync(string script)
    {
        try
        {
            BufferedCommandResult result = await Cli.Wrap("powershell")
                .WithArguments(new[] { "-NoProfile", "-NonInteractive", "-Command", script })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(CancellationToken.None);

            return result.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    public static async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(
        string executable,
        string arguments,
        CancellationToken ct
    )
    {
        BufferedCommandResult result = await Cli.Wrap(executable)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(ct);

        return (result.ExitCode, result.StandardOutput, result.StandardError);
    }

    public static async Task<int> RunCommandFireAndForgetAsync(string executable, string arguments)
    {
        try
        {
            BufferedCommandResult result = await Cli.Wrap(executable)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(CancellationToken.None);

            return result.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    public static async Task<(int ExitCode, string Output, string Error)> RunWithSessionAsync(
        string host,
        string username,
        string password,
        string scriptBlock,
        CancellationToken ct,
        Action<string>? onOutput = null
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(scriptBlock);

        string escapedHost = host.Replace("'", "''");
        string escapedUsername = username.Replace("'", "''");
        string escapedPassword = password.Replace("'", "''");

        string script =
            "$ErrorActionPreference = 'Stop'\n"
            + "$oldTrusted = (Get-Item WSMan:\\localhost\\Client\\TrustedHosts).Value\n"
            + "Set-Item WSMan:\\localhost\\Client\\TrustedHosts -Value '"
            + escapedHost
            + "' -Force\n"
            + "$cred = New-Object PSCredential('"
            + escapedUsername
            + "', (ConvertTo-SecureString '"
            + escapedPassword
            + "' -AsPlainText -Force))\n"
            + "$so = New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck\n"
            + "$s = New-PSSession -ComputerName '"
            + escapedHost
            + "' -Credential $cred -SessionOption $so -ErrorAction Stop\n"
            + "try {\n"
            + "    Invoke-Command -Session $s -ScriptBlock {\n"
            + "        "
            + scriptBlock
            + "\n"
            + "    }\n"
            + "} finally {\n"
            + "    Remove-PSSession $s -ErrorAction SilentlyContinue\n"
            + "    Set-Item WSMan:\\localhost\\Client\\TrustedHosts -Value $oldTrusted -Force -ErrorAction SilentlyContinue\n"
            + "}\n";

        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ps1");
        try
        {
            await File.WriteAllTextAsync(tempFile, script, ct);

            System.Text.StringBuilder outputBuilder = new System.Text.StringBuilder();
            System.Text.StringBuilder errorBuilder = new System.Text.StringBuilder();

            CommandResult result = await Cli.Wrap("pwsh")
                .WithArguments(
                    new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tempFile }
                )
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(
                    PipeTarget.ToDelegate(line =>
                    {
                        outputBuilder.AppendLine(line);
                        onOutput?.Invoke(line);
                    })
                )
                .WithStandardErrorPipe(
                    PipeTarget.ToDelegate(line =>
                    {
                        errorBuilder.AppendLine(line);
                    })
                )
                .ExecuteAsync(ct);

            return (result.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public static async Task<(int ExitCode, string Output, string Error)> CopyToSessionAsync(
        string host,
        string username,
        string password,
        string localPath,
        string remotePath,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(localPath);
        ArgumentNullException.ThrowIfNull(remotePath);

        string escapedHost = host.Replace("'", "''");
        string escapedUsername = username.Replace("'", "''");
        string escapedPassword = password.Replace("'", "''");
        string escapedLocalPath = localPath.Replace("'", "''");
        string escapedRemotePath = remotePath.Replace("'", "''");

        string script =
            "$ErrorActionPreference = 'Stop'\n"
            + "$oldTrusted = (Get-Item WSMan:\\localhost\\Client\\TrustedHosts).Value\n"
            + "Set-Item WSMan:\\localhost\\Client\\TrustedHosts -Value '"
            + escapedHost
            + "' -Force\n"
            + "$cred = New-Object PSCredential('"
            + escapedUsername
            + "', (ConvertTo-SecureString '"
            + escapedPassword
            + "' -AsPlainText -Force))\n"
            + "$so = New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck\n"
            + "$s = New-PSSession -ComputerName '"
            + escapedHost
            + "' -Credential $cred -SessionOption $so -ErrorAction Stop\n"
            + "try {\n"
            + "    Copy-Item -ToSession $s -Path '"
            + escapedLocalPath
            + "' -Destination '"
            + escapedRemotePath
            + "' -Recurse -Force\n"
            + "} finally {\n"
            + "    Remove-PSSession $s -ErrorAction SilentlyContinue\n"
            + "    Set-Item WSMan:\\localhost\\Client\\TrustedHosts -Value $oldTrusted -Force -ErrorAction SilentlyContinue\n"
            + "}\n";

        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ps1");
        try
        {
            await File.WriteAllTextAsync(tempFile, script, ct);

            BufferedCommandResult result = await Cli.Wrap("pwsh")
                .WithArguments(
                    new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tempFile }
                )
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            return (result.ExitCode, result.StandardOutput, result.StandardError);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
