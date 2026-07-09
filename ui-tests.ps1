param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0; $fail = 0; $results = @()

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++; $script:results += @{ name = $Name; status = "PASS" }
            Write-Host "  PASS: $Name" -ForegroundColor Green
        } else {
            $script:fail++; $script:results += @{ name = $Name; status = "FAIL"; detail = "$output" }
            Write-Host "  FAIL: $Name - $output" -ForegroundColor Red
        }
    } catch {
        $script:fail++; $script:results += @{ name = $Name; status = "FAIL"; detail = "$_" }
        Write-Host "  FAIL: $Name - $_" -ForegroundColor Red
    }
}

New-Item -ItemType Directory -Force -Path "screenshots" | Out-Null

Write-Host "`n=== Shell + existence ===" -ForegroundColor Cyan
Test-UI "Nav view exists"        { winapp ui wait-for "MainNavView" -a $AppPid -t 5000 }
Test-UI "Nav: Tasks"             { winapp ui wait-for "NavTasks" -a $AppPid -t 3000 }
Test-UI "Nav: Quick Launch"      { winapp ui wait-for "NavQuickLaunch" -a $AppPid -t 3000 }
Test-UI "Nav: Running"           { winapp ui wait-for "NavRunning" -a $AppPid -t 3000 }
Test-UI "Nav: Upcoming"          { winapp ui wait-for "NavUpcoming" -a $AppPid -t 3000 }
Test-UI "Nav: Assistant"         { winapp ui wait-for "NavAssistant" -a $AppPid -t 3000 }
Test-UI "Nav: Settings"          { winapp ui wait-for "NavSettings" -a $AppPid -t 3000 }
# The folder pane is a toggle (collapsed by default) and the TreeView itself has no
# custom AutomationId; reveal the pane only if it's hidden, then assert a real, always-
# present tree node ("Pinned Tasks") instead of a non-existent "FolderTree" id.
winapp ui search "Pinned Tasks" -a $AppPid 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    winapp ui invoke "ShowFoldersButton" -a $AppPid 2>$null | Out-Null
    Start-Sleep -Milliseconds 400
}
Test-UI "Folder tree exists"     { winapp ui wait-for "Pinned Tasks" -a $AppPid -t 3000 }
Test-UI "Search box exists"      { winapp ui wait-for "TaskSearchBox" -a $AppPid -t 3000 }
Test-UI "Task list exists"       { winapp ui wait-for "TaskList" -a $AppPid -t 3000 }
Test-UI "New task button exists" { winapp ui wait-for "NewTaskButton" -a $AppPid -t 3000 }
Test-UI "Refresh button exists"  { winapp ui wait-for "RefreshButton" -a $AppPid -t 3000 }
Test-UI "Import button exists"   { winapp ui wait-for "ImportButton" -a $AppPid -t 3000 }

winapp ui screenshot -a $AppPid -o "screenshots/01-tasks.png" 2>$null

Write-Host "`n=== Navigation ===" -ForegroundColor Cyan
Test-UI "Go to Running"          { winapp ui invoke "NavRunning" -a $AppPid }
Test-UI "Running list appears"   { winapp ui wait-for "RunningTaskList" -a $AppPid -t 4000 }
winapp ui screenshot -a $AppPid -o "screenshots/02-running.png" 2>$null
Test-UI "Go to Settings/About"   { winapp ui invoke "NavSettings" -a $AppPid }
Start-Sleep -Milliseconds 400
winapp ui screenshot -a $AppPid -o "screenshots/03-about.png" 2>$null
Test-UI "Back to Tasks"          { winapp ui invoke "NavTasks" -a $AppPid }
Test-UI "Task list returns"      { winapp ui wait-for "TaskList" -a $AppPid -t 4000 }

Write-Host "`n=== Search filter ===" -ForegroundColor Cyan
# TaskSearchBox is an AutoSuggestBox whose automation element is a Group with no
# ValuePattern, so set-value fails on it directly. Target its inner editable child
# (AutomationId "TextBox", unique on the Tasks page) and PROVE the filter works: the
# task-item count must collapse to zero for a no-match query and return when cleared.
function Get-TaskItemCount {
    $o = winapp ui search "TaskSummaryDto" -a $AppPid 2>&1 | Out-String
    if ($o -match 'Found (\d+) match') { return [int]$Matches[1] } else { return 0 }
}
$baselineCount = Get-TaskItemCount
Test-UI "Set search text (filters list)" {
    winapp ui set-value "TextBox" "zzz_no_such_task" -a $AppPid | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "could not type into search box inner edit (TextBox)" }
    Start-Sleep -Milliseconds 500
    $typed = (winapp ui get-value "TextBox" -a $AppPid 2>&1 | Out-String).Trim()
    if ($typed -notmatch 'zzz_no_such_task') { throw "search text not applied (got '$typed')" }
    if ($baselineCount -gt 0 -and (Get-TaskItemCount) -ne 0) {
        throw "filter did not reduce the task list (baseline was $baselineCount)"
    }
    winapp ui wait-for "TextBox" -a $AppPid -t 2000   # deterministic success terminator
}
Test-UI "Clear search text (restores list)" {
    winapp ui set-value "TextBox" "" -a $AppPid | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "could not clear search box inner edit (TextBox)" }
    Start-Sleep -Milliseconds 500
    if ($baselineCount -gt 0 -and (Get-TaskItemCount) -lt $baselineCount) {
        throw "task list did not restore after clearing the filter (baseline was $baselineCount)"
    }
    winapp ui wait-for "TextBox" -a $AppPid -t 2000   # deterministic success terminator
}

Write-Host "`n=== Editor dialog + create (raw XML) ===" -ForegroundColor Cyan
$taskName = "WT_UITest"
$folder = "\WinTaskerUITest"
$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Created by Task Scheduler Studio UI test</Description>
    <Author>UITest</Author>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>2026-01-01T09:00:00</StartBoundary>
      <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
    </CalendarTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <LogonType>InteractiveToken</LogonType>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>C:\Windows\System32\cmd.exe</Command>
      <Arguments>/c echo wintask-scheduler-ui-test</Arguments>
    </Exec>
  </Actions>
</Task>
"@

Test-UI "Open New task menu"     {
    winapp ui invoke "NewTaskButton" -a $AppPid | Out-Null
    winapp ui wait-for "NewBlankTask" -a $AppPid -t 3000
}
Test-UI "Choose Blank task"      { winapp ui invoke "NewBlankTask" -a $AppPid }
Test-UI "Editor name field"     { winapp ui wait-for "EditorName" -a $AppPid -t 5000 }
Test-UI "Set name"              { winapp ui set-value "EditorName" $taskName -a $AppPid }
# EditorFolder is an editable ComboBox (Group) with no ValuePattern; set its inner
# editable child (AutomationId "EditableText", unique while the editor dialog is open),
# which commits to the bound folder path (verified: the task lands under $folder).
Test-UI "Set folder"            {
    winapp ui set-value "EditableText" $folder -a $AppPid | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "could not set folder inner edit (EditableText)" }
    $fv = (winapp ui get-value "EditableText" -a $AppPid 2>&1 | Out-String).Trim()
    if ($fv -ne $folder) { throw "folder not applied (got '$fv')" }
}
Start-Sleep -Milliseconds 200
winapp ui screenshot -a $AppPid -o "screenshots/04-editor-general.png" 2>$null

Test-UI "Switch to Triggers tab" { winapp ui invoke "TabTriggers" -a $AppPid }
Test-UI "Add trigger visible"    { winapp ui wait-for "AddTriggerButton" -a $AppPid -t 3000 }
winapp ui screenshot -a $AppPid -o "screenshots/05-editor-triggers.png" 2>$null

Test-UI "Switch to Actions tab"  { winapp ui invoke "TabActions" -a $AppPid }
Test-UI "Add action visible"     { winapp ui wait-for "AddActionButton" -a $AppPid -t 3000 }

Test-UI "Switch to Advanced tab" { winapp ui invoke "TabAdvanced" -a $AppPid }
Test-UI "XML box visible"        { winapp ui wait-for "EditorXml" -a $AppPid -t 3000 }
Test-UI "Toggle raw XML on"      { winapp ui invoke "EditorUseRawXml" -a $AppPid }
Test-UI "Raw XML is on"          { winapp ui wait-for "EditorUseRawXml" -a $AppPid --value "On" -t 3000 }
Test-UI "Set XML"               { winapp ui set-value "EditorXml" $xml -a $AppPid }
winapp ui screenshot -a $AppPid -o "screenshots/06-editor-advanced.png" 2>$null

Test-UI "Save (invoke primary)"  { winapp ui invoke "Save" -a $AppPid }
Test-UI "Dialog closed"          { winapp ui wait-for "EditorName" -a $AppPid --gone -t 6000 }
Start-Sleep -Milliseconds 800

Write-Host "`n=== Backend verification (proves UI create hit the real store) ===" -ForegroundColor Cyan
$q = schtasks /query /tn "$folder\$taskName" /fo LIST 2>&1 | Out-String
if ($q -match [regex]::Escape("$folder\$taskName")) {
    $pass++; $results += @{ name = "Task created in Windows store"; status = "PASS" }
    Write-Host "  PASS: Task created in Windows store" -ForegroundColor Green
} else {
    $fail++; $results += @{ name = "Task created in Windows store"; status = "FAIL"; detail = $q }
    Write-Host "  FAIL: Task created in Windows store - $q" -ForegroundColor Red
}

winapp ui screenshot -a $AppPid -o "screenshots/07-after-create.png" 2>$null

Write-Host "`n=== Source stamp (created tasks are marked 'Task Scheduler Studio') ===" -ForegroundColor Cyan
# The task above was created via the raw-XML editor whose XML has NO <Source> element. The app
# must stamp RegistrationInfo <Source> with "Task Scheduler Studio" so it passes the "only Task
# Scheduler Studio tasks" filter (regressed for AI/raw-XML created tasks before this was added).
$xmlOut = schtasks /query /tn "$folder\$taskName" /xml 2>&1 | Out-String
if ($xmlOut -match '<Source>\s*Task Scheduler Studio\s*</Source>') {
    $pass++; $results += @{ name = "Created task is stamped as Task Scheduler Studio"; status = "PASS" }
    Write-Host "  PASS: Created task is stamped as Task Scheduler Studio" -ForegroundColor Green
} else {
    $fail++; $results += @{ name = "Created task is stamped as Task Scheduler Studio"; status = "FAIL"; detail = $xmlOut }
    Write-Host "  FAIL: Created task is NOT stamped as Task Scheduler Studio" -ForegroundColor Red
}

Write-Host "`n=== Visual designer ===" -ForegroundColor Cyan
Test-UI "Open Designer"          { winapp ui invoke "NavDesigner" -a $AppPid }
Test-UI "Designer name field"    { winapp ui wait-for "DesignerName" -a $AppPid -t 5000 }
Test-UI "Designer add trigger"   { winapp ui wait-for "DesignerAddTrigger" -a $AppPid -t 3000 }
Test-UI "Designer add action"    { winapp ui wait-for "DesignerAddAction" -a $AppPid -t 3000 }
Test-UI "Designer form editor"   { winapp ui wait-for "DesignerFormEditor" -a $AppPid -t 3000 }
Test-UI "Designer save button"   { winapp ui wait-for "DesignerSave" -a $AppPid -t 3000 }
Test-UI "Set designer name"      { winapp ui set-value "DesignerName" "WT_DesignerUITest" -a $AppPid }
Test-UI "Cancel designer"        { winapp ui invoke "DesignerCancel" -a $AppPid }
Test-UI "Designer returns to Tasks" { winapp ui wait-for "DesignerName" -a $AppPid --gone -t 5000 }

Write-Host "`n=== Script library ===" -ForegroundColor Cyan
Test-UI "Open Library"           { winapp ui invoke "NavLibrary" -a $AppPid }
Test-UI "Script list exists"     { winapp ui wait-for "ScriptList" -a $AppPid -t 5000 }
Test-UI "Library search exists"  { winapp ui wait-for "LibrarySearch" -a $AppPid -t 3000 }
Test-UI "New script button"      { winapp ui wait-for "NewScriptButton" -a $AppPid -t 3000 }
# Return to Tasks so the accessibility audit inspects the main page.
winapp ui invoke "NavTasks" -a $AppPid 2>$null | Out-Null
Start-Sleep -Milliseconds 300

Write-Host "`n=== Accessibility audit ===" -ForegroundColor Cyan
$inspectJson = winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
# winapp >=0.3 nests elements under .windows[].elements; older shapes exposed a flat .elements.
$allElements = @()
if ($inspectJson.windows) { $allElements = @($inspectJson.windows | ForEach-Object { $_.elements } | Where-Object { $_ }) }
if (-not $allElements -or $allElements.Count -eq 0) { $allElements = @($inspectJson.elements) }
$appElements = @($allElements | Where-Object {
    $_.type -match 'Button|TextBox|ComboBox|CheckBox|ToggleSwitch|TabItem|Edit' -and
    $_.name -notmatch 'Minimize|Maximize|Close|System|Restore' -and
    $_.className -notmatch 'PickerHost|#32770|CabinetWClass'
})
$missingId = @($appElements | Where-Object { -not $_.automationId })
if ($appElements.Count -gt 0 -and $missingId.Count -eq 0) {
    $pass++; $results += @{ name = "All app controls have AutomationId"; status = "PASS" }
    Write-Host "  PASS: AutomationId coverage ($($appElements.Count) controls checked)" -ForegroundColor Green
} elseif ($appElements.Count -eq 0) {
    $fail++; $results += @{ name = "AutomationId coverage"; status = "FAIL"; detail = "Inspector returned 0 interactive controls (JSON shape?)" }
    Write-Host "  FAIL: AutomationId audit inspected 0 controls" -ForegroundColor Red
} else {
    $fail++
    $names = ($missingId | ForEach-Object { "$($_.type) '$($_.name)'" }) -join ", "
    $results += @{ name = "AutomationId coverage"; status = "FAIL"; detail = "Missing: $names" }
    Write-Host "  WARN: AutomationId coverage - Missing: $names" -ForegroundColor Yellow
}

# Cleanup the test task, then remove the (now-empty) test folder via the Task Scheduler COM API.
schtasks /delete /tn "$folder\$taskName" /f 2>&1 | Out-Null
try {
    $svc = New-Object -ComObject Schedule.Service; $svc.Connect()
    $svc.GetFolder('\').DeleteFolder($folder.TrimStart('\'), 0)
} catch { }

Write-Host "`nPassed: $pass | Failed: $fail" -ForegroundColor Cyan
$results | ConvertTo-Json | Out-File "test-results.json"
if ($fail -gt 0) { exit 1 } else { exit 0 }
