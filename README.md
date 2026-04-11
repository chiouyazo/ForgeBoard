<p align="center">
  <img src="src/ForgeBoard/Assets/logo256.png" alt="ForgeBoard" width="128" />
</p>

<h1 align="center">ForgeBoard</h1>
<p align="center"><strong>Build, manage, and publish virtual machine images with HashiCorp Packer.</strong></p>

---

ForgeBoard is a desktop and browser application for orchestrating HashiCorp Packer builds. It provides a visual interface for defining build pipelines, managing base images, and publishing artifacts to feeds like Nexus or local file shares. Built with Uno Platform for cross-platform support and backed by an ASP.NET Core API.

## Features

- **Build Wizard** - Step-by-step UI for creating Packer build definitions
- **Step Library** - Reusable provisioning steps (PowerShell, Shell, file uploads, restarts) with import/export
- **Build Chaining** - Define builds that depend on other builds. ForgeBoard builds the dependency first, registers the artifact, then continues
- **Live Build Logs** - Real-time streaming of Packer output during builds via SignalR
- **Image Management** - Track base images, build artifacts, and disk usage across your storage
- **Feed Integration** - Import images from and publish artifacts to Nexus repositories, SMB shares, or local directories
- **Format Conversion** - Convert between VHDX, standalone VHDX, and Vagrant .box formats during publish
- **Dual-Mode Builds** - Direct PSSession provisioning for fast, reliable application setup; Packer for ISO-based OS installations
- **Upload Progress** - Real-time progress tracking with speed and ETA for Nexus uploads
- **Pre-build Validation** - Checks for Packer installation, builder availability, base image existence, and disk space before starting
- **Launch VM** - Create a Hyper-V VM directly from a build artifact with one click. Integrates with [VmManager](https://github.com/chiouyazo/VmManager) for ongoing VM management
- **Multi-platform** - Runs as a desktop app (Windows/Linux/macOS) or in the browser via WebAssembly

## Architecture

```
ForgeBoard.sln
  ForgeBoard/              Uno Platform UI (Desktop + WebAssembly)
  ForgeBoard.Api/          ASP.NET Core REST API + SignalR hub
  ForgeBoard.Core/         Business logic, Packer integration, LiteDB data layer
  ForgeBoard.Contracts/    Shared models and interfaces
  ForgeBoard.Tests/        NUnit test suite
```

The API runs as a standalone process on port 5050. The UI connects to it via HTTP and SignalR. In standalone mode, the API also serves the WASM frontend as static files.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [HashiCorp Packer](https://www.packer.io/downloads) (1.15+)
- **Windows with Hyper-V** for Hyper-V builders, or **QEMU** for QEMU builders

## Getting Started

### 1. Clone and build

```bash
git clone https://github.com/chiouyazo/ForgeBoard
cd ForgeBoard
dotnet build
```

### 2. Start the API

```bash
dotnet run --project ForgeBoard.Api
```

The API starts on `http://localhost:5050`. Swagger documentation is available at `/swagger`.

### 3. Run the UI

**Desktop:**

```bash
dotnet run --project ForgeBoard -f net10.0-desktop
```

**Browser (WebAssembly):**

```bash
dotnet run --project ForgeBoard -f net10.0-browserwasm
```

Then open `http://localhost:5000` in your browser.

### 4. Configure Packer path

Open Settings in the UI and set the path to your `packer.exe` binary.

## Configuration

The API reads configuration from `ForgeBoard.Api/appsettings.json`:

```json
{
  "ForgeBoard": {
    "DataDirectory": "D:\\ForgeBoard",
    "TempDirectory": "D:\\ForgeBoard\\temp"
  }
}
```

Leave empty to use defaults (`%LOCALAPPDATA%\ForgeBoard` on Windows).

## Standalone Deployment

To deploy ForgeBoard as a single process that serves both the API and the browser UI:

```bash
dotnet publish ForgeBoard -f net10.0-browserwasm -c Release
dotnet publish ForgeBoard.Api -c Release

xcopy /E /Y ForgeBoard\bin\Release\net10.0-browserwasm\publish\wwwroot\* ^
  ForgeBoard.Api\bin\Release\net10.0\publish\wwwroot\
```

On the target machine, run `dotnet ForgeBoard.Api.dll` and open `http://machine:5050`.

### Windows Service

ForgeBoard can run as a Windows service. The API has built-in support via `Microsoft.Extensions.Hosting.WindowsServices`:

```powershell
sc.exe create ForgeBoard binPath="C:\ForgeBoard\ForgeBoard.Api.exe" start=auto
sc.exe start ForgeBoard
```

### Installer

An Inno Setup installer script is included in `installer/ForgeBoard.iss`. Build it with:

```powershell
.\installer\build-installer.ps1 -Version "1.0.0"
```

The installer registers the Windows service, configures the port and data directory during setup, and creates Start Menu shortcuts. GitHub Actions builds the installer automatically on version tags (`v1.0.0`).

## Step Types

ForgeBoard supports several provisioning step types that map to Packer provisioners:

| Type               | Packer Provisioner    | Use Case                                          |
| ------------------ | --------------------- | ------------------------------------------------- |
| **PowerShell**     | `powershell` (inline) | Run PowerShell commands directly in the VM        |
| **PowerShellFile** | `powershell` (script) | Upload and execute a .ps1 file from the host      |
| **Shell**          | `shell` (inline)      | Run shell commands (Linux VMs)                    |
| **ShellFile**      | `shell` (script)      | Upload and execute a shell script from the host   |
| **FileUpload**     | `file`                | Copy files or directories from the host to the VM |
| **WindowsRestart** | `windows-restart`     | Restart Windows and wait for WinRM to reconnect   |

### Example: Install software via Chocolatey

**Type:** PowerShell

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-Expression ((New-Object Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))

choco install git vscode nodejs-lts -y --no-progress
```

### Example: Upload configuration files

**Type:** FileUpload

```
C:\builds\configs\app-settings
C:\ProgramData\MyApp\config
```

First line is the source path on the host. Second line is the destination inside the VM.

### Example: Configure a Windows service

**Type:** PowerShell

```powershell
sc.exe config 'MyService' obj= ".\Administrator" password= "P@ssw0rd"
Set-Service -Name 'MyService' -StartupType Automatic
Start-Service -Name 'MyService'
```

### Example: Run a SQL script from the host

**Type:** PowerShellFile

Point the content to a `.ps1` file on the host that contains your database setup logic. ForgeBoard uploads it to the VM and executes it.

## Build Chaining

When creating a build definition, you can select another build definition as the base image (shown as `[Build] OtherBuild` in the image picker). When you start the build:

1. ForgeBoard checks if the dependency build has an existing artifact
2. If not, it triggers the dependency build first and waits for it to complete
3. The dependency's output is registered as an artifact
4. The current build uses that artifact as its base image

This lets you create layered builds, for example a base OS image that gets extended with application-specific provisioning.

## Launch VM

Build artifacts can be launched as Hyper-V VMs directly from the image library. Click "Launch VM" on any build artifact to create a VM using a differencing disk (the original artifact is never modified).

The VM is created with:
- A full copy of the artifact VHDX (no dependency on the original - safe to delete the artifact afterwards)
- Generation 2, TPM enabled, Secure Boot off
- 4 GB RAM, 2 CPUs
- Connected to Default Switch
- Stored in `%USERPROFILE%\ForgeBoard-VMs\`

If [VmManager](https://github.com/chiouyazo/VmManager) is installed on the same machine, ForgeBoard automatically registers the VM in VmManager's `managed-vms.json` so you can manage it from there (rename, delete, snapshot, push).

## Publishing to Nexus (VmManager Integration)

ForgeBoard can publish build artifacts to a [Sonatype Nexus](https://www.sonatype.com/products/sonatype-nexus-repository) raw repository in a format compatible with [VmManager](https://github.com/chiouyazo/VmManager). This enables a full pipeline: build a VM image with ForgeBoard, publish it to Nexus, and deploy it with VmManager.

When publishing, ForgeBoard:

1. Optionally converts the artifact to a Vagrant `.box` file (tar.gz containing a VHDX + metadata.json), which is the format VmManager expects
2. Uploads the artifact to `{repository}/{imageId}/versions/{version}/{filename}`
3. Creates a version manifest at `{repository}/{imageId}/versions/{version}/manifest.json`
4. Creates a top-level manifest at `{repository}/{imageId}/manifest.json`

VmManager's `NexusCatalogAdapter` discovers images by reading these manifests. The publish dialog in ForgeBoard lets you pick the Nexus feed, repository, version tag, and output format.

### Supported output formats

| Format | Use case |
|--------|----------|
| **VHDX** | Direct use with Hyper-V, or as a base for further builds |
| **.box** | Vagrant-compatible archive for VmManager deployment |

The `.box` conversion runs the `ConvertVhd` post-processor (merges differencing disks into a standalone VHDX) followed by `CompressBox` (wraps the VHDX in a tar.gz with a `metadata.json`).

## Builders

ForgeBoard supports two Packer builder backends:

| Builder | Platform | Use case |
|---------|----------|----------|
| **Hyper-V** (`hyperv-vmcx`, `hyperv-iso`) | Windows with Hyper-V | Production Windows VM builds. Supports Generation 2 VMs, differencing disks, clone-from-VM. Requires bare metal or a VM with nested virtualization. |
| **QEMU** | Linux with KVM, or Windows (slow) | Linux VM builds on KVM-enabled hosts. Fast with KVM acceleration, slow without. Outputs qcow2, vmdk, or raw formats. |

Each step has a **Run via Packer** toggle. When off (the default), the step runs in Direct mode over a PSSession -- faster file copies, better error handling, and no HCL escaping headaches. Turn it on only for steps that need to run during the Packer build phase, like ISO installs with `unattend.xml`. For QEMU and ISO builds, all steps use Packer mode automatically.

For Windows image builds, Hyper-V is the recommended builder. QEMU is primarily useful on Linux build servers with KVM for building Linux guest images.

## Documentation

- **[Writing Build Steps](docs/writing-steps.md)** - How to write steps, use the HTTP file server, handle escaping, common patterns, and troubleshooting

## API Documentation

Interactive API documentation is available at `/swagger` when the API is running. The API provides endpoints for:

- **Builds** - Create, manage, and execute build definitions
- **Steps** - Manage the reusable step library with import/export
- **Images** - Track base images and build artifacts
- **Feeds** - Configure and browse Nexus, SMB, and local feeds
- **Settings** - Configure Packer path and application settings
- **Validation** - Check paths, feed connectivity, and available builders

## Tech Stack

- **UI Framework:** [Uno Platform](https://platform.uno/) 6.5 with Skia renderer
- **Backend:** ASP.NET Core 10
- **Database:** [LiteDB](https://www.litedb.org/) (embedded, zero-config)
- **Process Management:** [CliWrap](https://github.com/Tyrrrz/CliWrap)
- **Real-time:** SignalR
- **Logging:** Serilog
- **Testing:** NUnit + FluentAssertions

## License

See [LICENSE](LICENSE) for details.
