# Run manually in PowerShell. The management token is requested securely and is never saved.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$configPath = Join-Path $PSScriptRoot 'Resources/CloudAuthConfig.json'
$configuration = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$projectUri = [Uri]$configuration.projectUrl
if ($projectUri.Scheme -ne 'https' -or $projectUri.Host -notmatch '^([a-z]{20})\.supabase\.co$') {
    throw 'CloudAuthConfig.json must contain the Supabase project URL.'
}
$projectRef = $Matches[1]
$endpoint = 'https://api.supabase.com/v1/projects/' + $projectRef + '/config/auth'
$confirmation = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'EmailTemplates/ConfirmSignup.html'))
$recovery = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'EmailTemplates/ResetPassword.html'))
if (-not $confirmation.Contains('{{ .Token }}') -or -not $recovery.Contains('{{ .Token }}')) {
    throw 'Both email templates must contain the one-time code placeholder.'
}

Write-Host ('Project: ' + $projectRef)
Write-Host 'Updates only signup-confirmation and password-recovery email subjects and templates.'
Write-Host 'SMTP, account passwords, security requirements and game saves are unchanged.'
Write-Host 'Create a personal management access token at https://supabase.com/dashboard/account/tokens'
Write-Host 'Paste it into the hidden prompt below. Do not use a publishable or service_role key.'
$secureToken = Read-Host 'Supabase management access token (hidden)' -AsSecureString
$plainToken = $null
$headers = $null
$current = $null
$verified = $null
try {
    $plainToken = ([Net.NetworkCredential]::new('', $secureToken)).Password
    if ([string]::IsNullOrWhiteSpace($plainToken)) { throw 'No management token supplied.' }
    $headers = @{ Authorization = 'Bearer ' + $plainToken; Accept = 'application/json' }
    # Check access first; never print the full response, which can include private settings.
    $current = Invoke-RestMethod -Method Get -Uri $endpoint -Headers $headers -TimeoutSec 25 -MaximumRedirection 0
    $payload = @{
        mailer_subjects_confirmation = '驗證你的遊戲帳號'
        mailer_templates_confirmation_content = $confirmation
        mailer_subjects_recovery = '重設遊戲帳號密碼'
        mailer_templates_recovery_content = $recovery
    } | ConvertTo-Json
    $payloadBytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $null = Invoke-RestMethod -Method Patch -Uri $endpoint -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $payloadBytes -TimeoutSec 25 -MaximumRedirection 0
    $verified = Invoke-RestMethod -Method Get -Uri $endpoint -Headers $headers -TimeoutSec 25 -MaximumRedirection 0
    if ($verified.mailer_templates_confirmation_content -ne $confirmation -or
        $verified.mailer_templates_recovery_content -ne $recovery) {
        throw 'Template verification failed. Check the project dashboard before retrying.'
    }
    Write-Host 'SUCCESS: Both verification-code email templates are installed and verified.' -ForegroundColor Green
    if ([string]::IsNullOrWhiteSpace($verified.smtp_host)) {
        Write-Host 'Custom SMTP is not configured. Set it up before allowing general players to register.' -ForegroundColor Yellow
    } else {
        Write-Host 'Custom SMTP is configured; actual email delivery still needs an end-to-end test.'
    }
} catch {
    # Avoid echoing HTTP response bodies or request headers containing account configuration.
    throw 'Email setup did not complete. Check network access and management-token permissions, then check the Auth email templates in Supabase.'
} finally {
    $plainToken = $headers = $current = $verified = $null
    if ($secureToken -is [IDisposable]) { $secureToken.Dispose() }
}
