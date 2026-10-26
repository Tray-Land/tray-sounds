Describe 'GitHub release packaging' {
    BeforeAll {
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem

        # Load only the helpers, never the build/sign/upload entry point.
        $path = Join-Path (Split-Path -Parent $PSScriptRoot) 'build-msix-for-gh.ps1'
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) {
            throw ($parseErrors | Out-String)
        }
        $helpers = 'Read-ZipEntryText', 'New-AppInstallerFile', 'Add-AutoUpdateReference', 'Test-ReleaseBundle'
        foreach ($node in $ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $helpers
        }, $false)) {
            . ([scriptblock]::Create($node.Extent.Text))
        }

        function Add-TestZipText {
            param($Archive, [string]$Name, [string]$Text)
            $writer = [System.IO.StreamWriter]::new($Archive.CreateEntry($Name).Open())
            try { $writer.Write($Text) } finally { $writer.Dispose() }
        }

        function New-TestBundle {
            param(
                [string[]]$Architectures = @('x64', 'arm64'),
                [string]$EmbeddedFeed = [System.IO.File]::ReadAllText($feedPath)
            )

            $bundle = [System.IO.Compression.ZipFile]::Open($bundlePath, [System.IO.Compression.ZipArchiveMode]::Create)
            try {
                $entries = @()
                foreach ($architecture in $Architectures) {
                    $entries += "<Package Type='application' Architecture='$architecture' FileName='$architecture.msix' />"
                    $stream = [System.IO.MemoryStream]::new()
                    try {
                        $package = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
                        try {
                            $manifest = @"
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap13="http://schemas.microsoft.com/appx/manifest/uap/windows10/13">
  <Identity Name="$identityName" Publisher="$identityPublisher" Version="$version" ProcessorArchitecture="$architecture" />
  <Properties><uap13:AutoUpdate><uap13:AppInstaller File="Update.appinstaller" /></uap13:AutoUpdate></Properties>
</Package>
"@
                            Add-TestZipText $package 'AppxManifest.xml' $manifest
                            Add-TestZipText $package 'Update.appinstaller' $EmbeddedFeed
                            Add-TestZipText $package 'AppxBlockMap.xml' '<BlockMap />'
                        }
                        finally { $package.Dispose() }
                        $stream.Position = 0
                        $destination = $bundle.CreateEntry("$architecture.msix").Open()
                        try { $stream.CopyTo($destination) } finally { $destination.Dispose() }
                    }
                    finally { $stream.Dispose() }
                }
                Add-TestZipText $bundle 'AppxMetadata/AppxBundleManifest.xml' @"
<Bundle>
  <Identity Name="$identityName" Publisher="$identityPublisher" Version="$version" />
  <Packages>$($entries -join '')</Packages>
</Bundle>
"@
            }
            finally { $bundle.Dispose() }
        }
    }

    BeforeEach {
        $version = '1.2.3.0'
        $tag = 'v1.2.3'
        $identityName = 'ReleaseTests'
        $identityPublisher = 'CN=ReleaseTests'
        $feedName = 'TraySounds.appinstaller'
        $bundleName = 'TraySounds_1.2.3.msixbundle'
        $feedPath = Join-Path $TestDrive $feedName
        $bundlePath = Join-Path $TestDrive $bundleName
        if (Test-Path -LiteralPath $bundlePath) {
            Remove-Item -LiteralPath $bundlePath -Force
        }
        $frameworks = @('x64', 'arm64' | ForEach-Object {
            [pscustomobject]@{
                Name = 'Microsoft.WindowsAppRuntime.2'
                Version = '2000.1.2.0'
                Publisher = 'CN=RuntimeTests'
                Architecture = $_
                AssetName = "Runtime_$_.msix"
            }
        })
        New-AppInstallerFile -Path $feedPath -Repository 'Tray-Land/tray-sounds' -BundleName $bundleName -FrameworkPackages $frameworks
    }

    It 'keeps the update URL stable and pins bundle and runtime URLs to the release' {
        $identityPublisher = 'CN=Release & Tests'
        New-AppInstallerFile -Path $feedPath -Repository 'Tray-Land/tray-sounds' -BundleName $bundleName -FrameworkPackages $frameworks
        [xml]$feed = Get-Content -LiteralPath $feedPath -Raw
        $feed.AppInstaller.Uri | Should Be 'https://github.com/Tray-Land/tray-sounds/releases/latest/download/TraySounds.appinstaller'
        $feed.AppInstaller.MainBundle.Uri | Should Be 'https://github.com/Tray-Land/tray-sounds/releases/download/v1.2.3/TraySounds_1.2.3.msixbundle'
        $feed.AppInstaller.MainBundle.Publisher | Should Be $identityPublisher
        @($feed.AppInstaller.Dependencies.Package).Count | Should Be 2
        foreach ($framework in $frameworks) {
            $node = $feed.AppInstaller.Dependencies.Package | Where-Object ProcessorArchitecture -eq $framework.Architecture
            $node.Uri | Should Be "https://github.com/Tray-Land/tray-sounds/releases/download/v1.2.3/$($framework.AssetName)"
        }
        $feed.AppInstaller.UpdateSettings.OnLaunch.HoursBetweenUpdateChecks | Should Be '0'
        $feed.AppInstaller.UpdateSettings.OnLaunch.ShowPrompt | Should Be 'false'
        $feed.AppInstaller.UpdateSettings.OnLaunch.UpdateBlocksActivation | Should Be 'false'
        $feed.AppInstaller.UpdateSettings.LastChild.LocalName | Should Be 'AutomaticBackgroundTask'
    }

    It 'adds the embedded feed reference only once and preserves existing properties' {
        $manifestPath = Join-Path $TestDrive 'Package.appxmanifest'
        Set-Content -LiteralPath $manifestPath -Value @'
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" IgnorableNamespaces="uap">
  <Properties><DisplayName>Test app</DisplayName></Properties>
</Package>
'@
        Add-AutoUpdateReference -Path $manifestPath
        Add-AutoUpdateReference -Path $manifestPath
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
        $references = $manifest.GetElementsByTagName('AppInstaller', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/13')
        $references.Count | Should Be 1
        $references[0].GetAttribute('File') | Should Be 'Update.appinstaller'
        $manifest.Package.Properties.DisplayName | Should Be 'Test app'
        $manifest.Package.IgnorableNamespaces | Should Be 'uap uap13'
    }

    It 'accepts a matching dual-architecture bundle with embedded feeds and dependencies' {
        New-TestBundle
        { Test-ReleaseBundle -BundlePath $bundlePath -FeedPath $feedPath -FrameworkPackages $frameworks } | Should Not Throw
    }

    It 'rejects a bundle missing ARM64' {
        New-TestBundle -Architectures @('x64')
        { Test-ReleaseBundle -BundlePath $bundlePath -FeedPath $feedPath -FrameworkPackages $frameworks } |
            Should Throw 'The bundle is missing the arm64 application package.'
    }

    It 'rejects a package with a different embedded update feed' {
        New-TestBundle -EmbeddedFeed '<AppInstaller />'
        { Test-ReleaseBundle -BundlePath $bundlePath -FeedPath $feedPath -FrameworkPackages $frameworks } |
            Should Throw 'differs from the published feed'
    }

    It 'rejects a runtime dependency that differs from the release asset' {
        New-TestBundle
        $frameworks[1].AssetName = 'Wrong.msix'
        { Test-ReleaseBundle -BundlePath $bundlePath -FeedPath $feedPath -FrameworkPackages $frameworks } |
            Should Throw 'does not match its release asset'
    }
}
