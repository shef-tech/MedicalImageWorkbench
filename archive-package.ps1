$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$locationFile = Join-Path $artifactsRoot 'latest-package.txt'

if (-not (Test-Path -LiteralPath $locationFile -PathType Leaf)) {
    throw 'Package location file is missing. Publish the package first.'
}

$packagePath = (Get-Content -LiteralPath $locationFile -Raw).Trim()
$packageRoot = (Resolve-Path -LiteralPath $packagePath).Path
$resolvedArtifacts = (Resolve-Path -LiteralPath $artifactsRoot).Path

if ([IO.Path]::GetDirectoryName($packageRoot) -ne $resolvedArtifacts) {
    throw 'The package must be directly inside the artifacts folder.'
}

$requiredFiles = @(
    'api\Workbench.Api.exe',
    'api\efbundle.exe',
    'desktop\Workbench.Desktop.exe',
    'package-start-api.cmd',
    'package-start-desktop.cmd'
)

foreach ($relativePath in $requiredFiles) {
    $requiredPath = Join-Path $packageRoot $relativePath

    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required package file is missing: $relativePath"
    }
}

Add-Type -AssemblyName System.IO.Compression

$archiveName = 'MedicalImageWorkbench-win-x64-' +
    [Guid]::NewGuid().ToString('N') + '.zip'

$archivePath = Join-Path $artifactsRoot $archiveName

$archiveStream = [IO.File]::Open(
    $archivePath,
    [IO.FileMode]::CreateNew,
    [IO.FileAccess]::Write
)

try {
    $archive = [IO.Compression.ZipArchive]::new(
        $archiveStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $true
    )

    try {
        foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse) {
            $relativePath = $file.FullName.Substring(
                $packageRoot.Length
            ).TrimStart([char[]]@('\', '/'))

            # Exclude runtime data, databases, temporary files,
            # and debugging symbols.
            if ($relativePath -match '(^|[\\/])App_Data([\\/]|$)' -or
                $relativePath -match '\.(db|db-shm|db-wal|pdb|tmp)$') {
                continue
            }

            $entryName = $relativePath.Replace('\', '/')

            $entry = $archive.CreateEntry(
                $entryName,
                [IO.Compression.CompressionLevel]::Optimal
            )

            $inputStream = [IO.File]::OpenRead($file.FullName)

            try {
                $entryStream = $entry.Open()

                try {
                    $inputStream.CopyTo($entryStream)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
            finally {
                $inputStream.Dispose()
            }
        }

        $instructions = @'
Medical Image Analysis Workbench - Windows x64

1. Extract this entire ZIP into a writable local folder.
2. Run package-start-api.cmd.
3. Wait for "Now listening on: http://localhost:5050".
4. Run package-start-desktop.cmd.
5. Import your own PNG or JPEG images.

Keep the backend window open while using the desktop.
Close the desktop and press Ctrl+C in the backend to stop.

The package starts with an empty database. Its first backend
launch creates api\App_Data and applies database migrations.

Keep all supporting files together.
The .NET runtime is included in the application folders.

Runtime images, annotations, and job records are stored beneath
api\App_Data. Preserve that folder if you want to retain your data.

Do not move an already-used package without backing up its data:
the current application stores absolute image paths.

This is an educational research-image workbench, not a clinical
diagnostic application.

Source:
https://github.com/shef-tech/MedicalImageWorkbench
'@

        $readmeEntry = $archive.CreateEntry('START-HERE.txt')
        $readmeStream = $readmeEntry.Open()
        $writer = [IO.StreamWriter]::new($readmeStream)

        try {
            $writer.Write($instructions)
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $archiveStream.Dispose()
}

# Verify the finished archive.
$checkStream = [IO.File]::OpenRead($archivePath)

try {
    $checkArchive = [IO.Compression.ZipArchive]::new(
        $checkStream,
        [IO.Compression.ZipArchiveMode]::Read
    )

    try {
        $entryNames = @(
            $checkArchive.Entries |
                ForEach-Object { $_.FullName }
        )

        foreach ($relativePath in $requiredFiles) {
            $entryName = $relativePath.Replace('\', '/')

            if ($entryNames -notcontains $entryName) {
                throw "Archive is missing: $entryName"
            }
        }

        $unexpected = @(
            $entryNames | Where-Object {
                $_ -match '(^|/)App_Data(/|$)' -or
                $_ -match '\.(db|db-shm|db-wal|pdb|tmp)$'
            }
        )

        if ($unexpected.Count -gt 0) {
            throw 'Archive verification found excluded runtime files.'
        }
    }
    finally {
        $checkArchive.Dispose()
    }
}
finally {
    $checkStream.Dispose()
}

Set-Content -LiteralPath (
    Join-Path $artifactsRoot 'latest-archive.txt'
) -Value $archivePath

$archiveFile = Get-Item -LiteralPath $archivePath

Write-Host ''
Write-Host 'Clean package archive created and checked.'
Write-Host $archivePath
Write-Host ('Size: {0:N1} MB' -f ($archiveFile.Length / 1MB))
Write-Host 'Your original package and its data were preserved.'