using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ForgeBoard.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.Build;

public sealed class BuildFileServer
{
    private readonly ILogger<BuildFileServer> _logger;

    private static readonly ConcurrentDictionary<string, HttpListener> _activeFileServers =
        new ConcurrentDictionary<string, HttpListener>();

    public BuildFileServer(ILogger<BuildFileServer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string? Start(string executionId, BuildDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(definition);

        string? rootDir = ResolveFileRoot(definition);

        if (rootDir is null || !Directory.Exists(rootDir))
        {
            return null;
        }

        int port = 18585;
        string prefix = $"http://+:{port}/";

        try
        {
            EnsureFirewallRule(port);

            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            _activeFileServers[executionId] = listener;

            _ = Task.Run(async () => await RunListenerLoopAsync(listener, rootDir));

            _logger.LogInformation(
                "Started build file server on port {Port} serving {RootDir} for execution {ExecutionId}",
                port,
                rootDir,
                executionId
            );

            return port.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start build file server on port {Port}", port);
            return null;
        }
    }

    public void Stop(string executionId)
    {
        ArgumentNullException.ThrowIfNull(executionId);

        if (_activeFileServers.TryRemove(executionId, out HttpListener? listener))
        {
            try
            {
                listener.Stop();
                listener.Close();
                _logger.LogInformation(
                    "Stopped build file server for execution {ExecutionId}",
                    executionId
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error stopping build file server for {ExecutionId}",
                    executionId
                );
            }
        }
    }

    private static string? ResolveFileRoot(BuildDefinition definition)
    {
        foreach (BuildStep step in definition.Steps)
        {
            if (
                !step.Content.Contains("FORGEBOARD_FILE_SERVER", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            foreach (string line in step.Content.Split('\n'))
            {
                string trimmed = line.Trim();
                if (
                    trimmed.StartsWith(
                        "# FORGEBOARD_FILE_ROOT=",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return trimmed["# FORGEBOARD_FILE_ROOT=".Length..].Trim();
                }
            }
        }

        return null;
    }

    private static async Task RunListenerLoopAsync(HttpListener listener, string rootDir)
    {
        while (listener.IsListening)
        {
            try
            {
                HttpListenerContext ctx = await listener.GetContextAsync();
                string requestPath = Uri.UnescapeDataString(
                    ctx.Request.Url!.AbsolutePath.TrimStart('/')
                );
                string fullPath = Path.Combine(
                    rootDir,
                    requestPath.Replace('/', Path.DirectorySeparatorChar)
                );

                if (File.Exists(fullPath))
                {
                    ctx.Response.ContentType = "application/octet-stream";
                    ctx.Response.ContentLength64 = new FileInfo(fullPath).Length;
                    using (FileStream fs = File.OpenRead(fullPath))
                    {
                        await fs.CopyToAsync(ctx.Response.OutputStream);
                    }
                    ctx.Response.Close();
                }
                else if (Directory.Exists(fullPath))
                {
                    string[] entries = Directory.GetFileSystemEntries(fullPath);
                    string listing = string.Join(
                        "\n",
                        entries.Select(e =>
                        {
                            string relative = Path.GetRelativePath(rootDir, e)
                                .Replace(Path.DirectorySeparatorChar, '/');
                            bool isDir = Directory.Exists(e);
                            string encoded = string.Join(
                                "/",
                                relative.Split('/').Select(Uri.EscapeDataString)
                            );
                            return isDir ? encoded + "/" : encoded;
                        })
                    );
                    byte[] bytes = Encoding.UTF8.GetBytes(listing);
                    ctx.Response.ContentType = "text/plain";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void EnsureFirewallRule(int port)
    {
        try
        {
            System.Diagnostics.Process fwCheck = new System.Diagnostics.Process();
            fwCheck.StartInfo.FileName = "netsh";
            fwCheck.StartInfo.Arguments =
                $"advfirewall firewall show rule name=\"ForgeBoard Build File Server\"";
            fwCheck.StartInfo.UseShellExecute = false;
            fwCheck.StartInfo.RedirectStandardOutput = true;
            fwCheck.StartInfo.CreateNoWindow = true;
            fwCheck.Start();
            fwCheck.WaitForExit(5000);

            if (fwCheck.ExitCode != 0)
            {
                System.Diagnostics.Process fwAdd = new System.Diagnostics.Process();
                fwAdd.StartInfo.FileName = "netsh";
                fwAdd.StartInfo.Arguments =
                    $"advfirewall firewall add rule name=\"ForgeBoard Build File Server\" dir=in action=allow protocol=tcp localport={port}";
                fwAdd.StartInfo.UseShellExecute = false;
                fwAdd.StartInfo.RedirectStandardOutput = true;
                fwAdd.StartInfo.CreateNoWindow = true;
                fwAdd.Start();
                fwAdd.WaitForExit(5000);
                _logger.LogInformation(
                    "Added firewall rule for build file server on port {Port}",
                    port
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure firewall for build file server");
        }
    }
}
