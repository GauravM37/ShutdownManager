# Shutdown Manager

**Shutdown Manager** is a Windows desktop application that makes it easy to automate power actions on your computer. Create one-time, daily, or weekly schedules and let the application manage shutdown and other system operations automatically.

## Features

- **Multiple Power Actions** — Schedule Shutdown, Restart, Sleep, Hibernate, Log Off, and Lock.
- **Flexible Scheduling** — Create one-time, daily, and weekly schedules.
- **Countdown Timer** — Set a countdown before executing a power action.
- **Snooze and Cancel** — Snooze pending actions or cancel them before execution.
- **System Tray Integration** — Keep the application running in the system tray and access it when needed.
- **Start with Windows** — Configure the application to launch automatically with Windows.
- **Start Minimized** — Optionally launch directly into the system tray.
- **Force-Close Option** — Optionally force running applications to close during Shutdown and Restart operations.
- **Execution History** — Review previously executed and handled scheduled actions.
- **Dashboard** — View schedules and upcoming executions from one place.
- **Settings** — Configure application behavior and countdown preferences.

## Screenshots

_Add application screenshots here._

## Requirements

- Windows 10 or Windows 11
- 64-bit Windows (x64)
- No separate .NET installation is required when using the self-contained release build.

## Download

Download the latest Windows executable from the project's **[Releases](../../releases)** page.

1. Open the latest release.
2. Download the `ShutdownManager-win-x64.zip` asset.
3. Extract the ZIP file.
4. Run `ShutdownManager.exe`.

## Build from Source

### Prerequisites

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

### Clone the Repository

```powershell
git clone https://github.com/GauravM37/ShutdownManager.git
cd ShutdownManager
```

### Build the Application

```powershell
dotnet build .\ShutdownManager\ShutdownManager.csproj -c Release
```

### Publish a Self-Contained Single-File Build

Run:

```powershell
dotnet publish .\ShutdownManager\ShutdownManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The published application will be available in:

```text
ShutdownManager\bin\Release\net10.0-windows\win-x64\publish\
```

The publish folder contains the executable and any additional files required by the application.

## Configuration and Usage

1. Launch **ShutdownManager.exe**.
2. Create a schedule from the dashboard.
3. Select the required power action.
4. Choose a one-time, daily, or weekly schedule.
5. Configure the date, time, or weekdays as applicable.
6. Save the schedule.

The dashboard displays your configured schedules and upcoming executions. You can edit, cancel, or snooze schedules where applicable.

## Important Notes

- The force-close option is available for Shutdown and Restart operations. Enabling it may cause unsaved work in running applications to be lost.
- Save your work before a scheduled power action is executed.
- Sleep and Hibernate availability depends on the hardware and power capabilities supported by Windows.

## Technology

- C#
- .NET 10
- Windows Forms
- Windows Power Management
