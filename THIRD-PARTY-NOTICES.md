# Third-Party Notices

Windows Task Studio's own source code is licensed under the MIT License (see
[`LICENSE`](LICENSE)). The application uses the third-party components listed
below, each under its own license. This file is provided for attribution and
license compliance; the full license text for each component ships with its
package (via NuGet) and at the linked source.

| Component | Version | License |
|-----------|---------|---------|
| [TaskScheduler](https://github.com/dahall/TaskScheduler) (`Microsoft.Win32.TaskScheduler`, by David Hall) | 2.11.0 | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT |
| [CommunityToolkit.WinUI.Controls.Sizers](https://github.com/CommunityToolkit/Windows) | 8.2.x | MIT |
| [GitHub.Copilot.SDK](https://github.com/github/copilot-sdk) | 1.0.0-beta | MIT |
| Microsoft.Windows.SDK.BuildTools.WinApp | 0.3.2 | MIT |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools) | 10.0.x | Microsoft Windows SDK license (build-time tooling) |
| [Microsoft.WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK) | 2.1.3 | Microsoft Software License Terms — Windows App SDK (redistributable under its "Distributable Code" terms) |
| .NET runtime (bundled in the self-contained build) | net10.0 | MIT (.NET) |

## Notes

- The project's own code is MIT-licensed and contains no copyleft (GPL/LGPL/MPL)
  dependencies, so it imposes no viral licensing requirements.
- The **Windows App SDK** and **Windows SDK build tools** are Microsoft components
  governed by Microsoft's license terms. Those terms explicitly permit
  redistributing the Windows App SDK runtime as part of applications you develop,
  which is how the self-contained Windows Task Studio package distributes it.
- This list reflects the direct package references at the time of writing.
  Transitive dependencies retain their own (also permissive) licenses.
