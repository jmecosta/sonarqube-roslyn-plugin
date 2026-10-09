param (
    [string]$sourcePath,
    [string]$destinationPath
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

function ZipFiles($source, $destination) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    if (Test-Path $destination) {
        Remove-Item $destination -Force
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($source, $destination)
}

ZipFiles $sourcePath $destinationPath
