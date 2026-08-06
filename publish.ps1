<#
.SYNOPSIS
    Prepares this project for publishing to GitHub, and pushes it once you have approved
    what is about to go out.

.DESCRIPTION
    Runs in two steps on purpose.

    Step one — running it with no arguments — initialises the repository if needed, stages
    everything git is willing to track, and then STOPS and shows you the exact file list and
    commit message. Nothing is committed and nothing leaves the machine.

    Step two — running it again with -Push and the repository address — commits and pushes
    what you just read.

    Before either step it refuses to continue if the NASA API key, build output, or editor
    state has found its way into what would be published.

.PARAMETER RemoteUrl
    The GitHub repository to push to, for example
    https://github.com/<you>/persec2026-Windows-dev-interview-<your-name>.git

.PARAMETER Message
    Overrides the commit message. Leave it alone for the default, which is shown to you
    before anything is committed.

.PARAMETER Push
    Actually commit and push. Without this the script only shows you what would happen.

.EXAMPLE
    .\publish.ps1
    Stages everything and shows what would be published.

.EXAMPLE
    .\publish.ps1 -RemoteUrl https://github.com/me/persec2026-Windows-dev-interview-me.git -Push
    Commits and pushes after you have read the above.
#>

[CmdletBinding()]
param(
    [string] $RemoteUrl,
    [string] $Message,
    [switch] $Push
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = $PSScriptRoot
Set-Location $ProjectRoot

function Write-Step   ([string] $Text) { Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Good   ([string] $Text) { Write-Host "  [ok]   $Text" -ForegroundColor Green }
function Write-Note   ([string] $Text) { Write-Host "  $Text" -ForegroundColor Gray }
function Stop-WithReason ([string] $Text) {
    Write-Host "`n  [stop] $Text" -ForegroundColor Red
    exit 1
}

function Send-ToRemote ([string] $RemoteUrl) {
    $ExistingRemote = git remote get-url origin 2>$null

    if (-not $ExistingRemote) {
        git remote add origin $RemoteUrl
        Write-Good "remote 'origin' set to $RemoteUrl"
    }
    elseif ($ExistingRemote -ne $RemoteUrl) {
        git remote set-url origin $RemoteUrl
        Write-Good "remote 'origin' changed to $RemoteUrl"
    }

    $Branch = git rev-parse --abbrev-ref HEAD
    git push -u origin $Branch
    Write-Good "pushed to $Branch"

    Write-Host "`n  Done. Three things the brief still asks for:" -ForegroundColor Cyan
    Write-Host "    - the repository must be public" -ForegroundColor Gray
    Write-Host "    - send the link to hr.team@persec.co.th, subject: [yourname] practice Windows" -ForegroundColor Gray
    Write-Host "    - record a short demo video and include a way to watch it`n" -ForegroundColor Gray
}

# Checked before anything is touched. Asking to publish without saying where used to get as
# far as staging and then stop, which read as though something had been done.
if ($Push -and -not $RemoteUrl) {
    Stop-WithReason "-Push needs -RemoteUrl as well, so the script knows where to send it."
}

# --------------------------------------------------------------------------------------
# 1. The repository
# --------------------------------------------------------------------------------------
Write-Step "Repository"

if (-not (Test-Path (Join-Path $ProjectRoot '.git'))) {
    Write-Note "No repository here yet — creating one."
    git init --initial-branch=main | Out-Null
    Write-Good "initialised on branch 'main'"
}
else {
    Write-Good "already a repository"
}

# --------------------------------------------------------------------------------------
# 2. Stage, then look hard at what that produced
# --------------------------------------------------------------------------------------
Write-Step "Staging"

# Git narrates a line-ending conversion for every file the first time it stages one. The
# conversion is intended — .gitattributes asks for it — so the narration is only noise.
git add -A 2>$null
$Staged = @(git diff --cached --name-only)

if ($Staged.Count -eq 0) {
    $HasCommits = [bool](git rev-parse --verify HEAD 2>$null)

    if ($Push -and $HasCommits) {
        # Nothing new to record, but the request was to publish. Committed work that has not
        # reached the remote yet still needs sending.
        Write-Note "Nothing has changed, but there are commits — pushing those."
        Send-ToRemote -RemoteUrl $RemoteUrl
        exit 0
    }

    Write-Note "Nothing has changed since the last commit."
    exit 0
}

Write-Good "$($Staged.Count) file(s) staged"

# --------------------------------------------------------------------------------------
# 3. Refuse to publish anything that must not be published
# --------------------------------------------------------------------------------------
Write-Step "Safety checks"

# The API key ships with this repository on purpose, so the reviewer can run the program
# without registering for one. These are the two places it is meant to appear; anywhere else
# is an accident and still stops the publish.
$KeyFile = 'src/config/ApodApiConfig.json'
$IntendedKeyLocations = @($KeyFile, 'Build/ApodApiConfig.json')

$KeyPath = Join-Path $ProjectRoot $KeyFile
if (Test-Path $KeyPath) {
    $LocalKey = (Get-Content $KeyPath -Raw | ConvertFrom-Json).ApiKey

    if ($LocalKey -and $LocalKey -ne 'DEMO_KEY') {
        $Unexpected = @()
        foreach ($File in $Staged) {
            if ($IntendedKeyLocations -contains $File) { continue }

            $FullPath = Join-Path $ProjectRoot $File
            if (-not (Test-Path $FullPath)) { continue }

            # -Raw so a key split across a wrapped line is still found.
            $Content = Get-Content $FullPath -Raw -ErrorAction SilentlyContinue
            if ($Content -and $Content.Contains($LocalKey)) { $Unexpected += $File }
        }

        if ($Unexpected.Count -gt 0) {
            Stop-WithReason "The API key appears in a file it does not belong in: $($Unexpected -join ', '). Remove it before publishing."
        }

        $Shipping = $Staged | Where-Object { $IntendedKeyLocations -contains $_ }
        if ($Shipping) {
            Write-Host "  [note] The NASA API key will be published, in: $($Shipping -join ', ')" -ForegroundColor Yellow
            Write-Host "         That is deliberate — it lets the reviewer run the program as it is." -ForegroundColor Yellow
            Write-Host "         Revoke it at api.nasa.gov once the review is over." -ForegroundColor Yellow
        }
    }
}
Write-Good "the key appears only where it is meant to"

# Build output and editor state.
$Unwanted = $Staged | Where-Object { $_ -match '(^|/)(bin|obj|\.vs)/' }
if ($Unwanted) {
    Stop-WithReason "Build output or editor state is staged: $($Unwanted -join ', '). Check .gitignore."
}
Write-Good "no build output or editor state"

# GitHub refuses any file over 100 MB and warns above 50. The published executable carries the
# whole .NET runtime, so it is worth measuring rather than discovering at push time.
$Oversized = @()
$Large = @()
foreach ($File in $Staged) {
    $FullPath = Join-Path $ProjectRoot $File
    if (-not (Test-Path $FullPath)) { continue }

    $Bytes = (Get-Item $FullPath).Length
    if ($Bytes -gt 100MB) { $Oversized += "$File ($([math]::Round($Bytes/1MB,1)) MB)" }
    elseif ($Bytes -gt 50MB) { $Large += "$File ($([math]::Round($Bytes/1MB,1)) MB)" }
}

if ($Oversized.Count -gt 0) {
    Stop-WithReason "GitHub rejects files over 100 MB: $($Oversized -join ', ')."
}

if ($Large.Count -gt 0) {
    Write-Host "  [note] Over GitHub's 50 MB warning threshold: $($Large -join ', ')" -ForegroundColor Yellow
    Write-Host "         The push still works; GitHub will simply mention it." -ForegroundColor Yellow
}
Write-Good "no file exceeds GitHub's size limit"

# The repository name the brief asks for.
if ($RemoteUrl -and $RemoteUrl -notmatch 'persec2026-Windows-dev-interview-') {
    Write-Host "  [note] The brief asks for a repository named persec2026-Windows-dev-interview-<your-name>." -ForegroundColor Yellow
    Write-Host "         Yours is '$RemoteUrl'. Continuing, but check it is what you meant." -ForegroundColor Yellow
}

# --------------------------------------------------------------------------------------
# 4. The commit message
# --------------------------------------------------------------------------------------
$IsFirstCommit = -not (git rev-parse --verify HEAD 2>$null)

if (-not $Message) {
    if ($IsFirstCommit) {
        $Message = @'
NASA APOD viewer (WPF, .NET 10)

Change points
- Browse the APOD archive by choosing a start month and an end month; the whole
  span is fetched in one ranged request and shown a day at a time.
- Day list on the left, the day itself in the middle, and a Detail panel on the
  right carrying every field the response returned.
- Pictures load at screen resolution rather than print resolution, are decoded to
  the size actually displayed, and the surrounding days are read ahead so stepping
  is immediate.
- Videos are downloaded to a temporary file before playing, with play, pause, a
  scrub bar and looping.

Reasons
- One request per span rather than per day keeps well inside the API's rate limit.
- Screen-resolution pictures cut a month's traffic several times over on a slow
  connection without any visible difference.
- WPF's MediaElement does not reliably stream from the https addresses APOD uses,
  so the file is fetched first; this also makes seeking instant.
'@
    }
    else {
        $Message = "Update NASA APOD viewer`n`nChange points`n- (describe what changed)`n`nReasons`n- (why)"
    }
}

# --------------------------------------------------------------------------------------
# 5. Show it, and stop unless told to go ahead
# --------------------------------------------------------------------------------------
Write-Step "About to publish"

Write-Host "  Files:" -ForegroundColor Gray
$Staged | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

Write-Host "`n  Commit message:" -ForegroundColor Gray
$Message -split "`n" | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

if (-not $Push) {
    Write-Host "`n  Nothing has been committed and nothing has been sent." -ForegroundColor Yellow
    Write-Host "  Read the above. When it is right, run:" -ForegroundColor Yellow
    Write-Host "    .\publish.ps1 -RemoteUrl <your repository> -Push" -ForegroundColor White
    exit 0
}

# --------------------------------------------------------------------------------------
# 6. Commit and push
# --------------------------------------------------------------------------------------
Write-Step "Publishing"

git commit -m $Message | Out-Null
Write-Good "committed"

Send-ToRemote -RemoteUrl $RemoteUrl
