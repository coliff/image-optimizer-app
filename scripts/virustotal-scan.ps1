<#
.SYNOPSIS
  Uploads files to VirusTotal, waits for the scans and reports how many antivirus engines flag each one.
.DESCRIPTION
  Writes a table to the GitHub Actions job summary (when run there) and a warning for every flagged file.
  It never fails the build over a detection: false positives are expected while the app is unsigned and new.
  Needs a VirusTotal API key in $env:VIRUSTOTAL_API_KEY. The free key allows 4 requests a minute, so it paces itself.
#>
param(
  [Parameter(Mandatory)] [string[]]$Path,
  [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

if (-not $env:VIRUSTOTAL_API_KEY) {
  Write-Information 'VIRUSTOTAL_API_KEY is not set, so nothing was scanned.'
  return
}

$api = 'https://www.virustotal.com/api/v3'
$headers = @{ 'x-apikey' = $env:VIRUSTOTAL_API_KEY }

function Invoke-VirusTotal([string]$Uri, [string]$UploadFile) {
  for ($attempt = 1; ; $attempt++) {
    # Stay under the free key's 4 requests a minute.
    Start-Sleep -Seconds 16
    if ($UploadFile) {
      # Uploads go through curl: PowerShell's -Form quotes the multipart boundary, and VirusTotal's upload server
      # rejects that as a "Malformed multipart body". The key goes in a header file so it isn't on the command line.
      $headerFile = New-TemporaryFile
      $responseFile = New-TemporaryFile
      try {
        "x-apikey: $env:VIRUSTOTAL_API_KEY" | Out-File -FilePath $headerFile -Encoding ascii
        $status = curl --silent --show-error --header "@$headerFile" --form "file=@$UploadFile" `
          --output $responseFile --write-out '%{http_code}' $Uri
        if ($LASTEXITCODE -ne 0) { throw "curl couldn't upload $UploadFile (exit code $LASTEXITCODE)" }
        $response = Get-Content -Raw $responseFile
      } finally {
        Remove-Item $headerFile, $responseFile -ErrorAction SilentlyContinue
      }
      if ($status -eq '200') { return $response | ConvertFrom-Json }
    } else {
      try {
        return Invoke-RestMethod -Uri $Uri -Headers $headers
      } catch {
        $status = "$($_.Exception.Response.StatusCode.value__)"
        $response = $_.ErrorDetails.Message
      }
    }
    if ($status -eq '429' -and $attempt -lt 5) {
      Write-Information 'Rate limited by VirusTotal; waiting a minute.'
      Start-Sleep -Seconds 60
      continue
    }
    throw "VirusTotal returned HTTP $status for ${Uri}: $response"
  }
}

# GitHub Actions turns these into warnings on the run.
$warnings = [System.Collections.Generic.List[string]]::new()
$rows = foreach ($file in $Path | ForEach-Object { Get-Item $_ }) {
  $sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $link = "https://www.virustotal.com/gui/file/$sha256"
  Write-Information "Uploading $($file.Name) ($([math]::Round($file.Length / 1MB, 1)) MB, SHA256 $sha256)"

  # The upload URL works for files of any size up to 650 MB; the plain endpoint stops at 32 MB.
  $uploadUrl = (Invoke-VirusTotal -Uri "$api/files/upload_url").data
  $analysisId = (Invoke-VirusTotal -Uri $uploadUrl -UploadFile $file.FullName).data.id

  $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
  do {
    Start-Sleep -Seconds 15
    $analysis = (Invoke-VirusTotal -Uri "$api/analyses/$analysisId").data.attributes
  } while ($analysis.status -ne 'completed' -and (Get-Date) -lt $deadline)

  if ($analysis.status -ne 'completed') {
    $warnings.Add("::warning title=VirusTotal::$($file.Name) was still being scanned after $TimeoutMinutes minutes: $link")
    [pscustomobject]@{ File = $file.Name; Result = 'Still scanning'; Flagged = ''; Link = $link }
    continue
  }

  $stats = $analysis.stats
  $engines = $stats.malicious + $stats.suspicious + $stats.undetected + $stats.harmless
  $flagged = @($analysis.results.PSObject.Properties.Value |
      Where-Object { $_.category -in 'malicious', 'suspicious' } |
      ForEach-Object { "$($_.engine_name) ($($_.result))" })
  $detections = $stats.malicious + $stats.suspicious
  Write-Information "  $detections of $engines engines flagged it"
  if ($detections -gt 0) {
    $warnings.Add("::warning title=VirusTotal::$($file.Name) is flagged by $($flagged -join ', '): $link")
  }
  [pscustomobject]@{ File = $file.Name; Result = "$detections / $engines"; Flagged = $flagged -join ', '; Link = $link }
}

if ($env:GITHUB_STEP_SUMMARY) {
  $summary = @('## VirusTotal', '', '| File | Flagged by | Engines | Report |', '| --- | --- | --- | --- |')
  $summary += $rows | ForEach-Object { "| $($_.File) | $(if ($_.Flagged) { $_.Flagged } else { 'none' }) | $($_.Result) | [View]($($_.Link)) |" }
  $summary -join "`n" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
$rows | Format-Table -AutoSize | Out-String | Write-Information
$warnings | Write-Output
