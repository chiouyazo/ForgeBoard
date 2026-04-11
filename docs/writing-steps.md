# Writing Build Steps

This guide covers how to write build steps for ForgeBoard, including the different step types, common patterns, and things to watch out for.

## How Steps Work

ForgeBoard converts your build steps into a HashiCorp Packer template (HCL). Each step becomes a Packer provisioner. When you start a build, ForgeBoard generates the template, validates it, and runs `packer build`.

Steps run **inside the VM** via WinRM (for Windows) or SSH (for Linux). Your PowerShell code executes in the VM, not on the host machine.

## Build Modes

Each step has a **Run via Packer** toggle (off by default). This controls how the step gets executed during the build.

### Direct mode (default)

When the toggle is off, ForgeBoard runs the step over a PSSession directly to the VM. This is the default and recommended mode for most steps. Direct mode:

- Uses `Copy-Item -ToSession` for file copies (native PowerShell remoting, native PowerShell remoting)
- Uses `Invoke-Command -Session` for running scripts
- No HCL template generation, so no `${...}` escaping issues or here-string limitations
- Scripts run with `$ErrorActionPreference = 'Continue'` by default (native PowerShell remoting). Set it to `'Stop'` in your script if you want strict error handling.
- The build fails if a step returns a non-zero exit code

Direct mode is what you want for application installs, config file drops, service setup, SQL scripts, and basically everything that isn't an OS install.

### Writing scripts for Direct mode

Your script runs inside `Invoke-Command -Session $s -ScriptBlock { ... }` on the VM. Keep these things in mind:

- **Error handling is `Continue` by default.** Commands that write to stderr (like `npm`, `choco`) won't fail the build. If you want strict mode, add `$ErrorActionPreference = 'Stop'` at the top of your script content.
- **Exit codes matter.** The build checks `$LASTEXITCODE` after your script. If it's non-zero, the step fails. Use `exit 0` at the end if your script might leave a non-zero exit code from a sub-command.
- **PATH isn't refreshed automatically.** After installing software that modifies PATH (Chocolatey, Node.js, SQL Server), refresh it: `$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path', 'User')`
- **Each step gets a fresh PSSession.** Variables, working directory, and loaded modules don't persist between steps. If step 2 needs something from step 1, write it to a file.
- **File writes after certain installs.** Some software installations can corrupt the .NET runtime in the WinRM session. If `Set-Content` stops working, use `[System.IO.File]::WriteAllBytes()` instead.
- **No host interaction.** Scripts run non-interactively. Don't use `Read-Host` or any command that expects user input.
- **ForgeBoard cleans up after itself.** TrustedHosts, temporary VMs, differencing disks, and firewall rules are restored/removed after the build completes (success or failure).

### Packer mode

When the toggle is on, the step gets embedded in the Packer HCL template and runs as a normal Packer provisioner (same behavior as before). You need this for:

- ISO-based OS installations that require `unattend.xml` (the Packer `hyperv-iso` builder handles the boot sequence)
- Any step that must run during the initial Packer build phase before a PSSession is available

### Execution order

Packer steps always run first (as part of `packer build`), then Direct steps run in order after Packer finishes. If your build has a mix of both, keep this ordering in mind when planning dependencies between steps.

### QEMU and ISO builds

For QEMU builds or ISO base images (`hyperv-iso`), all steps are forced to Packer mode automatically regardless of the toggle. These build types need Packer to manage the full VM lifecycle from boot, so Direct mode isn't available.

## Step Types

| Type | Direct mode | Packer mode |
|------|-------------|-------------|
| **PowerShell** | Runs via `Invoke-Command -Session` with `$ErrorActionPreference = 'Stop'` | Embedded as inline HCL array (watch out for `${...}` escaping and here-strings) |
| **PowerShellFile** | Copies `.ps1` to VM via `Copy-Item -ToSession`, then runs it via `Invoke-Command` | Packer uploads and executes the `.ps1` via WinRM |
| **FileUpload** | Uses `Copy-Item -ToSession` (fast, handles large dirs well) | Uses Packer's `file` provisioner over WinRM (slow for large transfers) |
| **WindowsRestart** | Sends `Restart-Computer`, polls until VM responds | Packer's `windows-restart` provisioner handles the restart and WinRM reconnect |
| **Shell / ShellFile** | Not supported in Direct mode (Linux VMs use Packer mode) | Standard Packer shell provisioner over SSH |

### PowerShell (inline)

Your script content is embedded directly in the Packer template as inline commands. Each line becomes a separate array element in the HCL `inline` block.

```powershell
Write-Host 'Installing .NET Runtime...'
choco install dotnet-runtime -y --no-progress
Write-Host 'Done'
```

**Limitations:**
- Avoid PowerShell here-strings (`@"..."@`). Packer's inline provisioner splits your script by lines, which breaks the here-string syntax because the closing `"@` must have no leading whitespace.
- Use `ConvertTo-Json` instead of here-strings for building JSON.
- Dollar signs with curly braces like `${env:ProgramFiles}` get interpreted as HCL template interpolation. ForgeBoard escapes `${` to `$${` automatically, but be aware of this if you're debugging generated templates.

### PowerShellFile (script)

Points to a `.ps1` file **on the host machine**. Packer uploads the file to the VM and runs it. This avoids all the inline escaping issues.

**Content:** Full path to the `.ps1` file on the host.

```
C:\builds\scripts\install-sql-server.ps1
```

Use this for complex scripts. The file is uploaded as-is, so here-strings, special characters, and multi-line strings all work normally.

### FileUpload

Copies a file or directory from the host to the VM using Packer's `file` provisioner. This uses WinRM under the hood, which is slow for large files or many small files.

**Content format:**
```
<source path on host>
<destination path in VM>
```

First line is the source, second line is the destination. If no destination is specified, defaults to `C:\<filename>`.

```
C:\builds\configs\appsettings.json
C:\ProgramData\MyApp\appsettings.json
```

For directories:
```
C:\builds\my-configs
C:\ProgramData\MyApp\configs
```

**Performance note:** WinRM file transfer is slow, especially for large directories. For directories over ~50 MB, consider using the HTTP file server approach (see below) or packaging files into a zip.

### WindowsRestart

Triggers a Windows restart and waits for WinRM to reconnect. No content needed. Set the timeout to allow enough time for the restart and reconnect (typically 300 seconds is fine).

### Shell / ShellFile

Same as PowerShell/PowerShellFile but for Linux VMs using bash.

## HTTP File Server

> **Note:** The HTTP file server is only used in Packer mode. In Direct mode, `Copy-Item -ToSession` handles file transfers natively and is faster for most workloads. You only need the file server if your step runs through Packer.

For Hyper-V builds, ForgeBoard can automatically start a temporary HTTP file server on the host that serves files to the VM. This is much faster than Packer's WinRM-based `file` provisioner for large transfers.

### How to use it

1. Add a comment `# FORGEBOARD_FILE_SERVER` anywhere in your PowerShell step. This tells ForgeBoard to start the file server.
2. Add a comment `# FORGEBOARD_FILE_ROOT=C:\path\to\files` specifying the root directory to serve.
3. In your script, download files from `http://<hostIP>:18585/`.

**Example:**
```powershell
# FORGEBOARD_FILE_SERVER marker - tells ForgeBoard to start the HTTP file server
# FORGEBOARD_FILE_ROOT=C:\my-build-files

$hostIp = (Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Select-Object -First 1).NextHop
$baseUrl = "http://${hostIp}:18585"

# Download a single file
curl.exe -s -o "C:\install\setup.exe" "$baseUrl/installers/setup.exe"

# Download a directory listing and iterate
$listing = (Invoke-WebRequest -Uri "$baseUrl/configs/" -UseBasicParsing).Content
foreach ($entry in $listing -split "`n") {
    $entry = $entry.Trim()
    if (-not $entry) { continue }
    $decoded = [Uri]::UnescapeDataString(($entry -split '/')[-1])
    curl.exe -s -o "C:\install\$decoded" "$baseUrl/$entry"
}
```

**Details:**
- The file server starts automatically before the Packer build and stops after the build finishes (success or failure).
- It serves on port 18585. ForgeBoard creates a Windows Firewall rule for this port automatically.
- The directory listing endpoint returns one entry per line, URL-encoded. Directories end with `/`.
- Use `curl.exe` (built into Windows) instead of `Invoke-WebRequest` for file downloads. It's significantly faster for large files.
- The host IP is typically `172.17.80.1` (Hyper-V Default Switch gateway). The script above detects it automatically.

## Build Chaining

A build definition can use another build's output as its base image. In the build wizard, select a `[Build] <name>` entry as the base image.

When you start the build:
1. ForgeBoard checks if the dependency build already has an artifact.
2. If not, it triggers the dependency build first and waits for it to complete.
3. Both artifacts are registered and visible in the artifacts list.

This lets you create layered builds. For example, a base "Windows + SQL Server" image that a "Windows + SQL Server + MyApp" image builds on top of.

## Post-Processors

Post-processors run after Packer finishes. They operate on the build output (typically a VHDX file).

| Name | What it does |
|------|-------------|
| **ConvertVhd** | Merges differencing disks into a standalone VHDX using `Convert-VHD` |
| **CompressBox** | Creates a Vagrant `.box` archive (tar.gz with metadata.json) |
| **Checksum** | Computes SHA256 checksum of the output file |

Post-processors run in the order listed in the build definition. A common pipeline: `ConvertVhd` -> `CompressBox` -> `Checksum`.

### Publishing to Nexus for VmManager

If you plan to deploy images with [VmManager](https://github.com/chiouyazo/VmManager), add the `CompressBox` post-processor to your build. VmManager expects `.box` files (a tar.gz containing a VHDX and `metadata.json`).

A typical post-processor pipeline for VmManager-compatible output:
1. `ConvertVhd` - merges the differencing disk chain into a standalone VHDX
2. `CompressBox` - wraps the VHDX into a `.box` archive
3. `Checksum` - generates a SHA256 hash for integrity verification

After the build, use the Publish button on the build detail page to push the `.box` to a Nexus raw repository. ForgeBoard creates the directory structure and manifests that VmManager's `NexusCatalogAdapter` expects.

## Common Patterns

### Installing software from the internet

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$url = 'https://example.com/installer.exe'
$path = 'C:\install\installer.exe'
New-Item -ItemType Directory -Path C:\install -Force | Out-Null
Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
Start-Process -FilePath $path -ArgumentList '/S' -Wait -NoNewWindow
Remove-Item $path -ErrorAction SilentlyContinue
```

### Writing config files safely (WinRM sessions)

Some software installations can corrupt the .NET runtime in the WinRM session. If `Set-Content` fails with encoding errors, use `WriteAllBytes` instead:

```powershell
$content = '{"setting": "value"}'
$bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
[System.IO.File]::WriteAllBytes('C:\ProgramData\MyApp\config.json', $bytes)
```

### Building JSON without here-strings

Here-strings (`@"..."@`) break in Packer's inline provisioner. Use hashtables and `ConvertTo-Json` instead:

```powershell
$config = @{
    Server = 'localhost'
    Port = 5000
    Debug = $false
}
$json = $config | ConvertTo-Json -Depth 3
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
[System.IO.File]::WriteAllBytes('C:\ProgramData\MyApp\config.json', $bytes)
```

### Running SQL scripts

```powershell
$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine')
$sqlcmd = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
if (-not $sqlcmd) {
    $found = Get-ChildItem "${env:ProgramFiles}\Microsoft SQL Server" -Recurse -Filter sqlcmd.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $env:Path += ";$($found.DirectoryName)" }
}
sqlcmd -S ".\SQLEXPRESS" -C -E -i "C:\install\setup.sql"
```

### Configuring Windows services

```powershell
sc.exe config 'MyService' obj= ".\Administrator" password= "P@ssw0rd"
Set-Service -Name 'MyService' -StartupType Automatic
```

### Registering URL ACLs

```powershell
$urls = @('http://+:5000/', 'https://+:5001/')
foreach ($url in $urls) {
    netsh http add urlacl url=$url user=Everyone 2>&1 | Out-Null
}
```

## Timeouts

Each step has a timeout in seconds. If the step doesn't complete within this time, Packer cancels it and the build fails. Set timeouts generously:

| Step type | Suggested timeout |
|-----------|------------------|
| File copy (small) | 120s |
| File copy (large, HTTP) | 1800s |
| Software install | 600s |
| SQL Server install | 1800s |
| Windows restart | 300s |
| Quick config change | 60s |

## Troubleshooting

**"Extra characters after interpolation expression"** - Your PowerShell uses `${...}` which HCL interprets as template interpolation. ForgeBoard escapes this automatically, but if you see this error, check the generated template in the workspace directory.

**"White space is not allowed before the string terminator"** - You used a PowerShell here-string (`@"..."@`) in an inline step. Replace it with `ConvertTo-Json` or move the script to a `.ps1` file and use the PowerShellFile step type.

**"The system cannot find the path specified"** - A FileUpload step points to a source path that doesn't exist on the host. Check the first line of the step content.

**Slow file transfers** - WinRM file transfer is inherently slow. For directories over 50 MB, use the HTTP file server approach with `curl.exe` for downloads.

**Build hangs at "Waiting for WinRM"** - The VM isn't reachable. Check that the Hyper-V Default Switch exists and that WinRM is configured in the base image (the base image must have WinRM enabled with the correct credentials).

**PSSession connection timeout** - Direct mode steps connect via `New-PSSession` to the VM's IP. If this times out, check that the VM has finished booting, that WinRM is enabled, and that the Hyper-V Default Switch is assigning an IP. You can test manually with `Enter-PSSession -ComputerName <vmIP> -Credential $cred`. Also verify that Windows Firewall on the VM allows WinRM (TCP 5985/5986).

**Copy-Item fails for large directories** - `Copy-Item -ToSession` can be flaky with deeply nested directory trees or very large file counts. If you hit this, try zipping the directory first and extracting inside the VM, or break the copy into smaller chunks. For single large files (multi-GB), it generally works fine -- the issue is more about thousands of small files in a deep tree.
