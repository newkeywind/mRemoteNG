<#
    commit_and_push.ps1
    Commits and pushes startup performance fixes, startup-dialog layout fixes,
    exe-relative data storage, the tab tooltip change, the single-panel tab
    hiding, and the startup connections-source setting for mRemoteNG.

    USAGE  (run on a machine that has git; PowerShell 5.1+):
        powershell -ExecutionPolicy Bypass -File .\commit_and_push.ps1
        powershell -ExecutionPolicy Bypass -File .\commit_and_push.ps1 -RepoPath C:\src\mRemoteNG
        powershell -ExecutionPolicy Bypass -File .\commit_and_push.ps1 -Branch v1.78.2-dev -NoPush

    OPTIONS
        -RepoPath <dir>   Path to the git clone. Defaults to the script's own folder.
        -Remote <name>    Remote to push to. Default: origin.
        -Branch <name>    Branch to push to. Default: the current branch.
        -Message <text>   Override the commit message.
        -NoPush           Commit only; do not push.
        -StageAll         Stage every change (git add -A) instead of the explicit file list.

    NOTE
        The script stages an explicit list of files changed by this work. After committing it
        prints `git status --short`; if anything remains listed there, it was NOT committed --
        add those paths to $files below (or re-run with -StageAll) and commit again.
#>
[CmdletBinding()]
param(
    [string]$RepoPath = "",
    [string]$Remote = "origin",
    [string]$Branch = "",
    [string]$Message = "",
    [switch]$NoPush,
    [switch]$StageAll
)

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw ("git {0} failed with exit code {1}" -f ($GitArgs -join ' '), $LASTEXITCODE)
    }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git was not found on PATH. Install Git (or run this on a machine that has it)."
}

if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    if ($PSScriptRoot) { $RepoPath = $PSScriptRoot } else { $RepoPath = (Get-Location).Path }
}

Push-Location -LiteralPath $RepoPath
try {
    if (-not (Test-Path -LiteralPath ".git")) {
        throw "'$RepoPath' does not look like a git clone (no .git folder). Use -RepoPath to point at your clone."
    }

    # ---------------------------------------------------------------------
    # Files changed by this work. Add any extra paths here if needed.
    # ---------------------------------------------------------------------
    $files = @(
        "CHANGELOG.md",
        "Directory.Packages.props",
        "mRemoteNG/mRemoteNG.csproj",
        "mRemoteNG/App/Info/SettingsFileInfo.cs",
        "mRemoteNG/App/Initialization/CredsAndConsSetup.cs",
        "mRemoteNG/App/Logger.cs",
        "mRemoteNG/App/PerfLogger.cs",
        "mRemoteNG/App/Runtime.cs",
        "mRemoteNG/Config/Connections/ProfileConnectionsLoader.cs",
        "mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlConnectionsDeserializer.cs",
        "mRemoteNG/Config/Settings/Providers/PortableSettingsProvider.cs",
        "mRemoteNG/Config/Settings/SettingsLoader.cs",
        "mRemoteNG/Connection/ConnectionsService.cs",
        "mRemoteNG/Connection/DefaultConnectionInheritance.cs",
        "mRemoteNG/Connection/DefaultConnectionInfo.cs",
        "mRemoteNG/Language/Language.Designer.cs",
        "mRemoteNG/Language/Language.resx",
        "mRemoteNG/Tree/Root/RootNodeInfo.cs",
        "mRemoteNG/Plugins/PluginService.cs",
        "mRemoteNG/Properties/OptionsConnectionsPage.Designer.cs",
        "mRemoteNG/Properties/OptionsConnectionsPage.settings",
        "mRemoteNG/Properties/OptionsStartupExitPage.Designer.cs",
        "mRemoteNG/Properties/OptionsStartupExitPage.settings",
        "mRemoteNG/Properties/Settings.PortableProvider.cs",
        "mRemoteNG/UI/Controls/ConnectionTree/ConnectionTree.cs",
        "mRemoteNG/UI/Forms/FrmPassword.cs",
        "mRemoteNG/UI/Forms/FrmPassword.Designer.cs",
        "mRemoteNG/UI/Forms/OptionsPages/StartupExitPage.cs",
        "mRemoteNG/UI/Forms/OptionsPages/StartupExitPage.Designer.cs",
        "mRemoteNG/UI/Forms/ProfileSelectionDialog.cs",
        "mRemoteNG/UI/Forms/frmMain.cs",
        "mRemoteNG/UI/Window/ConnectionTreeWindow.cs",
        "mRemoteNG/UI/Window/ConnectionWindow.cs",
        "mRemoteNGTests/Tree/ConnectionTreeModelTests.cs",
        "mRemoteNGTests/Tree/RootNodeInfoTests.cs",
        "mRemoteNGTests/UI/Forms/OptionsPages/OptionsStartupExitPageTests.cs",
        "mRemoteNGTests/UI/Forms/PasswordFormTests.cs"
    )

    if ([string]::IsNullOrWhiteSpace($Branch)) {
        $Branch = (git rev-parse --abbrev-ref HEAD).Trim()
    }

    if ([string]::IsNullOrWhiteSpace($Message)) {
        $Message =
            "faster startup, non-clipped startup dialogs, exe-relative data storage, tab tunnel tooltip`n`n" +
            "- Cache/lazily decrypt connection strings and load profiles/connections in parallel; add PerfLogger timings`n" +
            "- Silence noisy plugin/assembly scan and stale default-property warnings during startup`n" +
            "- FrmPassword: auto-size rows so OK/Cancel stay visible at high text scaling / DPI`n" +
            "- ProfileSelectionDialog: DPI-aware fixed layout so the OK button is never clipped`n" +
            "- Store all data and settings files (databases, connection file, logs, options) next to the executable in both editions`n" +
            "- Store the options page settings in the portable settings file next to the executable instead of a per-user user.config`n" +
            "- Fix startup 'Couldn't add panel' crash by initializing the window list before loading the panel layout`n" +
            "- Connection tabs with an SSH tunnel show only the connection name; the via-tunnel info is in the tab tooltip`n" +
            "- Hide the redundant panel tab for a single connection panel; panel tabs return with two or more panels`n" +
            "- Fix System.Security.Cryptography.Pkcs DoS advisories (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7): pin the package and the self-contained runtime pack to 10.0.13`n" +
            "- Add a 'Load connections from' selector to the Startup/Exit options page (connections file vs. encrypted profile database); the mode is applied on every startup without prompting`n" +
            "- Persist renames of the connections tree root node: the root name change now raises a change notification so it is autosaved"
    }

    Write-Host "Repository : $RepoPath"
    Write-Host "Branch     : $Branch"
    Write-Host "Remote     : $Remote"
    Write-Host ""
    Write-Host "== git status (before) =="
    git status --short

    if ($StageAll) {
        Write-Host ""
        Write-Host "Staging ALL changes (-StageAll) ..."
        Invoke-Git add -A
    }
    else {
        Write-Host ""
        Write-Host "Staging the changed files ..."
        foreach ($f in $files) {
            if (Test-Path -LiteralPath $f) {
                Invoke-Git add -- "$f"
            }
            else {
                Write-Warning "Expected file not found, skipping: $f"
            }
        }
    }

    Write-Host ""
    Write-Host "== staged changes =="
    git diff --cached --stat

    & git diff --cached --quiet
    $hasStaged = ($LASTEXITCODE -ne 0)
    if (-not $hasStaged) {
        Write-Warning "Nothing is staged -- nothing to commit. (Do your files live in a different clone?)"
        return
    }

    Invoke-Git commit -m $Message

    if (-not $NoPush) {
        Invoke-Git push $Remote "HEAD:$Branch"
        Write-Host ""
        Write-Host ("Pushed to {0}/{1}." -f $Remote, $Branch) -ForegroundColor Green
    }
    else {
        Write-Host ""
        Write-Host "Committed locally. Push skipped (-NoPush)." -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "== git status (after) =="
    git status --short
    Write-Host ""
    Write-Host "Review the list above. Any remaining entries were NOT part of this commit," -ForegroundColor Yellow
    Write-Host "add them to `$files in this script (or re-run with -StageAll) if they belong with it." -ForegroundColor Yellow
}
finally {
    Pop-Location
}
