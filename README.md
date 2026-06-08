# Windows Tasker

A beautiful, Fluent **WinUI 3** replacement for the Windows Task Scheduler, plus a local **MCP
server** so AI agents can manage scheduled tasks. Both share one engine and write to the **live
Windows Task Scheduler store**, so every task is fully interoperable with the built‑in Task
Scheduler — create a task in either tool and it appears, runs, and edits identically in the other.

![Tasks](screenshots/01-tasks.png)

## Why it's cross‑compatible

Windows Tasker talks to the **Task Scheduler V2 COM API** (`Schedule.Service`) through the mature
`Microsoft.Win32.TaskScheduler` managed wrapper. That is the *same* API and the *same* task store
the built‑in Task Scheduler uses — there is no separate database. Anything Windows Tasker creates is
a normal scheduled task: visible in `taskschd.msc`, queryable with `schtasks`, and backed by the
standard Task Scheduler XML interchange format (Import/Export compatible).

## Solution layout

| Project | Type | Purpose |
|---------|------|---------|
| `Tasker.Core` | class library | The single source of truth: `TaskerService` wraps the COM API and exposes serializable DTOs (folders, tasks, triggers, actions, settings). |
| `Tasker.App` | WinUI 3 app | The desktop UI — folder tree, searchable task table, rich detail pane, a tabbed create/edit editor with a raw‑XML escape hatch, and a natural‑language **Copilot assistant**. |
| `Tasker.Mcp` | console app | A local stdio **MCP** server exposing the same engine to agents. |

Because the UI and the MCP server both call into `Tasker.Core`, they always behave identically.

## Create tasks with natural language (GitHub Copilot SDK)

The **Assistant** page embeds the official [GitHub Copilot SDK](https://github.com/github/copilot-sdk)
(`GitHub.Copilot.SDK`). Describe what you want in plain English — *"run backup.cmd every weekday at
6pm"* — and the Copilot agent runs a guided flow: it asks for anything that's missing (name, the
program to run, the schedule), confirms, then calls the Windows Tasker tools (`create_task`,
`create_task_from_xml`, `list_folders`, `list_tasks`) — the **same tools the MCP server exposes**,
backed by the shared engine — to write the task into the live Windows store.

- **Authentication:** click **Sign in with GitHub** on the Assistant page — it uses the GitHub CLI
  (`gh`) to authenticate interactively, reusing an existing `gh` session when present or opening the
  GitHub web sign-in otherwise. You can also paste a GitHub token, or just start chatting to use your
  existing Copilot session. A Copilot subscription is required.
- **Locked down:** the assistant is hardened on multiple layers — a dedicated **AGENTS.md** and
  system prompt restrict it to scheduled-task work, and a strict **permission handler** allows ONLY
  the Windows Tasker task tools to execute, rejecting shell, file, web, memory and every other
  built-in capability (verified: a jailbreak prompt asking it to run a shell command was refused and
  nothing happened). Tool inputs are validated, and destructive actions require confirmation.
- **Suggested prompts:** quick chips like *Scan tasks for red flags* (runs `analyze_task` over your
  tasks and reports evidence-based findings), *What runs at startup?*, and *Run Notepad daily*.

## Build & run

Prerequisites: Windows 10 1903+, .NET 10 SDK, Developer Mode, and the `winapp` CLI
(`winget install Microsoft.WinAppCli`).

```powershell
# Build everything
dotnet build WindowsTasker.slnx -c Debug

# Run the desktop app (uses the WinUI dev workflow helper)
cd Tasker.App
./BuildAndRun.ps1          # or: winapp run <build-output-folder>
```

> **Elevation:** Creating tasks that *Run with highest privileges*, or editing protected system
> tasks under `\Microsoft\Windows\…`, requires running Windows Tasker (or the MCP server) **as
> administrator** — exactly like the built‑in tool. Everyday per‑user tasks work without elevation.

## The MCP server

`Tasker.Mcp` speaks the Model Context Protocol over stdio (newline‑delimited JSON‑RPC 2.0).

### Tools

| Tool | Description |
|------|-------------|
| `list_folders` | The Task Scheduler folder tree with task counts. |
| `list_tasks` | Tasks in a folder (optionally recursive / filtered) with state, next/last run, result, and trigger/action summaries. |
| `get_task` | Full definition of one task incl. raw XML. |
| `export_task_xml` | Portable Task Scheduler XML for a task. |
| `analyze_task` | **Grounded, evidence-based assessment** of a task — a risk level plus findings that each cite the exact command/trigger/principal/setting they're based on (e.g. encoded PowerShell, download-and-run, runs from a user-writable path, hidden+elevated+autostart). |
| `run_task` / `stop_task` | Start / end a task on demand. |
| `enable_task` / `disable_task` | Toggle a task. |
| `delete_task` | Permanently delete a task. |
| `get_running_tasks` | Currently running instances (with engine PID). |
| `create_task` | Create or update a task from structured `triggers`+`actions` **or** raw `xml`. |

### Register it with an MCP client

**Easiest:** open the app's **About** page and click **Install for Copilot CLI** or **Install for
VS Code** — each merges a `windows-tasker` entry into the right config file (preserving anything
already there) pointing at the bundled server. Restart the CLI/editor afterward.

To do it manually, point your MCP client at the built server executable.

**GitHub Copilot CLI** — `~/.copilot/mcp-config.json`:

```json
{
  "mcpServers": {
    "windows-tasker": {
      "type": "stdio",
      "command": "D:\\Windows Tasker\\Tasker.App\\bin\\x64\\Debug\\net10.0-windows10.0.26100.0\\win-x64\\mcp\\Tasker.Mcp.exe",
      "args": [],
      "tools": []
    }
  }
}
```

You can also add it from the CLI with `copilot` then `/mcp add`, or use any built `Tasker.Mcp.exe`
path (e.g. `Tasker.Mcp\bin\Debug\net10.0-windows10.0.26100.0\Tasker.Mcp.exe`).

**VS Code** (Agent mode) — `%APPDATA%\Code\User\mcp.json` (or per-workspace `.vscode/mcp.json`):

```json
{
  "servers": {
    "windows-tasker": {
      "type": "stdio",
      "command": "D:\\Windows Tasker\\Tasker.App\\bin\\x64\\Debug\\net10.0-windows10.0.26100.0\\win-x64\\mcp\\Tasker.Mcp.exe",
      "args": []
    }
  }
}
```

**Generic / other clients** — any MCP host that launches a local stdio server works:

```json
{
  "mcpServers": {
    "windows-tasker": {
      "command": "dotnet",
      "args": ["run", "--project", "D:\\Windows Tasker\\Tasker.Mcp\\Tasker.Mcp.csproj", "-c", "Debug"]
    }
  }
}
```

### Example agent call

```jsonc
// tools/call create_task
{
  "name": "Nightly backup",
  "folder": "\\MyApps",
  "description": "Runs the backup script every night",
  "principal": { "runWithHighestPrivileges": false },
  "triggers": [
    { "kind": "daily", "startBoundary": "2026-01-01T02:00:00", "daysInterval": 1 }
  ],
  "actions": [
    { "command": "C:\\Scripts\\backup.cmd", "arguments": "--full" }
  ]
}
```

The structured editor and `create_task` understand the common trigger kinds (`oneTime`, `daily`,
`weekly`, `monthly`, `atStartup`, `atLogOn`, `onIdle`). For anything else — event triggers, COM
handler actions, exotic settings — pass full **raw XML** (UI: *Advanced* tab; MCP: the `xml` field),
which is registered verbatim for 100% fidelity.

## Quality-of-life extras (beyond the built-in Task Scheduler)

- **Natural-language task creation & management** via the Copilot assistant — it can also run,
  enable/disable, delete, and answer questions about your tasks ("what runs at startup?").
- **Run History** — per-task event log (from `Microsoft-Windows-TaskScheduler/Operational`) in the
  detail pane.
- **Folder picker** when creating/editing, plus **create/delete folders** from the tree toolbar.
- **Duplicate task**, and **templates** ("New task" ▾) for common recipes.
- **Sortable columns** (click a header) and **live search** across name, action, trigger, author.
- **Multi-select bulk actions** — run / enable / disable / delete several tasks at once.
- **Rich trigger editor** — one-time, daily, weekly, monthly (by day), monthly (by day-of-week),
  at startup, at logon, on idle, on an event, on session state change, plus repetition and expiry.
- **Run as a different user** — interactive, stored-password, S4U, or SYSTEM.
- **Backup / restore** a whole folder of tasks as a zip of XML.
- **Upcoming runs** view (everything sorted by next run time) and **toast notifications** when a
  task you started finishes.
- **Restart as administrator** for elevated operations; **auto-refresh** when the assistant changes
  tasks; **markdown-rendered** assistant replies.
- **Appearance** (About page) — switch the window **backdrop** (Mica Alt · Mica · Acrylic) and the
  **theme** (System · Light · Dark); choices persist across restarts.
- **Resizable details pane** — drag the splitter on the Tasks page to grow the inspection/XML area.

## Tests

`ui-tests.ps1` drives the running app through `winapp ui` automation (navigation, the editor, an
end‑to‑end create verified against `schtasks`, and an AutomationId accessibility audit):

```powershell
./ui-tests.ps1 -AppPid <pid-of-running-app>
```
