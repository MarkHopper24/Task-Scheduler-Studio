# Privacy Policy — Windows Tasker

**Effective date:** June 7, 2026

Windows Tasker ("the app") is a desktop application for Windows that creates,
edits, runs, and monitors Windows scheduled tasks. This policy explains what
data the app handles and how.

**Summary:** The app runs locally on your device. The developer does not operate
any server and does **not** collect, receive, store, or sell your personal data.
The app functions fully offline except for two optional features you explicitly
choose to use: the AI assistant and GitHub sign-in.

## Information the app handles

### Stored locally on your device
- **App settings and preferences** — for example theme, window material, the
  default start page, your pinned Quick Launch tasks, the "show only tasks
  created by Windows Tasker" filter, run-on-sign-in, and whether AI features are
  enabled — are saved in the app's local settings on your device.
- **Scheduled tasks** you create or edit are written to the Windows Task
  Scheduler store on your device — the same store used by the built-in Task
  Scheduler. They are not transmitted to the developer.
- **A GitHub token**, if you choose to save one, is stored in the Windows
  Credential Locker (Credential Manager) on your device.

The developer has no access to any of the above.

### Optional AI assistant (GitHub Copilot)
The Assistant is optional and can be turned off in Settings. When AI features are
disabled, the app makes no Copilot calls. If you use the Assistant:
- Your prompts and relevant scheduled-task context (such as task names, paths,
  states, and trigger/action summaries) are sent to GitHub's Copilot service to
  generate responses, through the GitHub Copilot SDK.
- This requires signing in to GitHub and a GitHub Copilot subscription.
- That data is processed by GitHub under GitHub's own privacy practices. See the
  [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)
  and the
  [GitHub Copilot documentation](https://docs.github.com/copilot). The developer
  does not receive or store this data.

### GitHub sign-in
Signing in uses the GitHub CLI (`gh`) or a token you paste. Authentication is
handled by GitHub. Credentials are stored locally (in the Windows Credential
Locker and/or your `gh` configuration) and are not sent to the developer.

### Local MCP server
The app includes a local Model Context Protocol (MCP) server that runs on your
device and communicates over standard input/output with a local client you
configure. It does not make network connections of its own.

### Platform diagnostic data
The app is built on the Windows App SDK and the .NET runtime and runs on Windows.
These Microsoft platform components, and Windows itself, may collect diagnostic
or telemetry data and send it to Microsoft under Microsoft's terms and the
[Microsoft Privacy Statement](https://aka.ms/privacy). This collection is governed
by Microsoft — not by the developer — and you can manage it through your Windows
privacy settings.

## Data the developer collects

**None.** The developer operates no backend service and includes no analytics,
telemetry, tracking, or advertising in the app.

## Children

The app is a general-purpose system utility and is not directed to children.

## Permissions and network use

The app uses Windows scheduled-task APIs (some operations may require running as
administrator) and accesses the network only for the optional AI assistant and
GitHub sign-in.

## Changes to this policy

This policy may be updated from time to time. Material changes will be reflected
in this document with a new effective date.

## Contact

Questions about this policy can be raised by opening an issue on the project's
GitHub repository.

---

**Disclaimer:** Windows Tasker is an individual personal project and proof of
concept created by a Microsoft employee. It is **not** an official Microsoft
product, service, or offering, and it is **not affiliated with, endorsed by, or
supported by Microsoft**.
