# Claude Usage Tray — Version 7

A lightweight Windows notification-area application that reads Claude usage from Home Assistant.

## Version 7 changes

- Keeps the Claude logo as the normal notification-area icon.
- Replaces the Claude logo with the current percentage while the pointer is over the icon.
- Shows a compact hover details window at the same time.
- Displays the hover details as `76% | Reset in 3h 24m`.
- Updates the reset countdown live while the hover details window is visible.
- Supports reset entities containing an ISO date/time, Unix timestamp, time of day, `TimeSpan`, or text such as `3h 24m`.
- Restores the Claude logo and closes the hover details window when the pointer moves away.
- Retains the configurable tray percentage font size.
- Keeps single-click manual refresh and the minimum two-second spinner animation.
- Migrates existing Version 6 settings automatically.

## Requirements

- Windows 10 or Windows 11, 64-bit
- Home Assistant accessible from the Windows PC
- A Claude usage entity, defaulting to:
  - `sensor.claude_usage_sam_pro_session_usage`
- A reset-time entity, defaulting to:
  - `sensor.claude_usage_sam_pro_session_reset_time`
- A Home Assistant long-lived access token
- Internet access to NuGet.org while publishing the source
- A compatible .NET 8 SDK when compiling the source

The published self-contained executable does not require .NET to be installed separately.

## Publish the application

Close any running Claude Usage Tray instance, then open PowerShell in the extracted project folder and run:

```powershell
# version 7
Set-ExecutionPolicy -Scope Process Bypass
.\publish-win-x64.ps1
```

The completed application is written to:

```text
publish\win-x64\ClaudeUsageTray.exe
```

## Tray controls

- **Hover:** replaces the Claude logo with the current percentage and shows the percentage plus live reset countdown above the taskbar.
- **Move away:** closes the hover details window and restores the Claude logo.
- **Single left-click:** manually refreshes usage and shows the spinner for at least two seconds.
- **Right-click:** opens the context menu.
- **Automatic refresh:** runs at the configured interval without displaying the spinner.

## Hover countdown examples

```text
76% | Reset in 3h 24m
76% | Reset in 42m
76% | Reset due
76% | Reset unavailable
```
