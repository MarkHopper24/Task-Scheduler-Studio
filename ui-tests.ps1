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
Test-UI "Nav: Running"           { winapp ui wait-for "NavRunning" -a $AppPid -t 3000 }
Test-UI "Nav: About"             { winapp ui wait-for "NavAbout" -a $AppPid -t 3000 }
Test-UI "Folder tree exists"     { winapp ui wait-for "FolderTree" -a $AppPid -t 3000 }
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
Test-UI "Go to About"            { winapp ui invoke "NavAbout" -a $AppPid }
Start-Sleep -Milliseconds 400
winapp ui screenshot -a $AppPid -o "screenshots/03-about.png" 2>$null
Test-UI "Back to Tasks"          { winapp ui invoke "NavTasks" -a $AppPid }
Test-UI "Task list returns"      { winapp ui wait-for "TaskList" -a $AppPid -t 4000 }

Write-Host "`n=== Search filter ===" -ForegroundColor Cyan
Test-UI "Set search text"        { winapp ui set-value "TaskSearchBox" "zzz_no_such_task" -a $AppPid }
Start-Sleep -Milliseconds 300
Test-UI "Clear search text"      { winapp ui set-value "TaskSearchBox" "" -a $AppPid }

Write-Host "`n=== Editor dialog + create (raw XML) ===" -ForegroundColor Cyan
$taskName = "WT_UITest"
$folder = "\WinTaskerUITest"
$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Created by Windows Tasker UI test</Description>
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
      <Arguments>/c echo windows-tasker-ui-test</Arguments>
    </Exec>
  </Actions>
</Task>
"@

Test-UI "Open editor"            { winapp ui invoke "NewTaskButton" -a $AppPid }
Test-UI "Editor name field"     { winapp ui wait-for "EditorName" -a $AppPid -t 4000 }
Test-UI "Set name"              { winapp ui set-value "EditorName" $taskName -a $AppPid }
Test-UI "Set folder"            { winapp ui set-value "EditorFolder" $folder -a $AppPid }
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

Write-Host "`n=== Accessibility audit ===" -ForegroundColor Cyan
$allElements = (winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json).elements
$appElements = @($allElements | Where-Object {
    $_.type -match 'Button|TextBox|ComboBox|CheckBox|ToggleSwitch|TabItem|Edit' -and
    $_.name -notmatch 'Minimize|Maximize|Close|System|Restore' -and
    $_.className -notmatch 'PickerHost|#32770|CabinetWClass'
})
$missingId = @($appElements | Where-Object { -not $_.automationId })
if ($missingId.Count -eq 0) {
    $pass++; $results += @{ name = "All app controls have AutomationId"; status = "PASS" }
    Write-Host "  PASS: AutomationId coverage" -ForegroundColor Green
} else {
    $fail++
    $names = ($missingId | ForEach-Object { "$($_.type) '$($_.name)'" }) -join ", "
    $results += @{ name = "AutomationId coverage"; status = "FAIL"; detail = "Missing: $names" }
    Write-Host "  WARN: AutomationId coverage - Missing: $names" -ForegroundColor Yellow
}

# Cleanup the test task/folder
schtasks /delete /tn "$folder\$taskName" /f 2>&1 | Out-Null

Write-Host "`nPassed: $pass | Failed: $fail" -ForegroundColor Cyan
$results | ConvertTo-Json | Out-File "test-results.json"
if ($fail -gt 0) { exit 1 } else { exit 0 }
