#!/usr/bin/env pwsh
#Requires -Version 5.1

param(
    [ValidateSet('AppVeyor','Build','CI','PackageRestore','Packages','Restore','Test',
                 '_Packages','_Publish','_PushMyGet','_SetVersion','_SignPackages','_Test32','_Test64')]
    [string]$target = "Test",
    [string]$configuration = "Release",
    [string]$buildAssemblyVersion = "",
    [string]$buildSemanticVersion = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$needsMono = ($PSVersionTable.PSVersion.Major -gt 5) -and (-not $PSVersionTable.OS.StartsWith("Windows"))
$dirSeparator = $([IO.Path]::DirectorySeparatorChar)

if ($PSScriptRoot -eq $null) {
    write-host "This build script requires PowerShell 3 or later." -ForegroundColor Red
    exit -1
}

$buildModuleFile = join-path $PSScriptRoot "tools/build/xunit-build-module.psm1"

if ((test-path $buildModuleFile) -eq $false) {
    write-host "Could not find build module. Did you forget to 'git submodule update --init'?" -ForegroundColor Red
    exit -1
}

Set-StrictMode -Version 2
Import-Module $buildModuleFile -Scope Local -Force -ArgumentList "4.8.1"
Set-Location $PSScriptRoot

$packageOutputFolder = (join-path (Get-Location) "artifacts/packages")
$parallelFlags = "-parallel all -maxthreads 16"
$nonparallelFlags = "-parallel collections -maxthreads 16"
$testOutputFolder = (join-path (Get-Location) "artifacts/test")
$solutionFolder = Get-Location

$signClientVersion = "0.9.1"
$signClientFolder = (join-path (Get-Location) "packages/SignClient.$signClientVersion")
$signClientAppSettings = (join-path (Get-Location) "tools/SignClient/appsettings.json")

# Helper functions

function _xunit_x64([string]$command) {
    $execString = "src/xunit.console/bin/" + $configuration + "/net472/xunit.console.exe " + $command
    if ($needsMono) {
        $execString = "mono $($execString)"
    }
    _exec $execString
}

function _xunit_x86([string]$command) {
    $execString = "src/xunit.console/bin/" + $configuration + "_x86/net472/xunit.console.x86.exe " + $command
    if ($needsMono) {
        $execString = "mono $($execString)"
    }
    _exec $execString
}

# Top-level targets

function __target_appveyor() {
    __target_ci
    __target__signpackages
    __target__pushmyget
}

function __target_build() {
    __target_restore

    _build_step "Compiling binaries"
        _exec ("dotnet build --configuration " + $configuration)
        _exec ("dotnet build src/xunit.console/xunit.console.csproj --configuration " + $configuration + "_x86")
}

function __target_ci() {
    $script:parallelFlags = "-parallel none -maxthreads 1"
    $script:nonparallelFlags = "-parallel none -maxthreads 1"

    __target__setversion
    __target_test
    __target__publish
    __target__packages
}

function __target_packagerestore() {
    __target_restore
}

function __target_packages() {
    __target_build
    __target__publish
    __target__packages
}

function __target_restore() {
    _build_step "Restoring NuGet packages"
       _exec "dotnet restore"
}

function __target_test() {
    __target_build
    __target__test32
    __target__test64
}

# Dependent targets

function __target__packages() {
    _build_step "Creating NuGet packages"
        Get-ChildItem -Recurse -Filter *.nuspec | _nuget_pack -outputFolder $packageOutputFolder -configuration $configuration
}

function __target__publish() {
    _build_step "Publishing projects for packaging"
        _msbuild "src/xunit.console/xunit.console.csproj /p:TargetFramework=netcoreapp1.0" $configuration "publish"
        _msbuild "src/xunit.console/xunit.console.csproj /p:TargetFramework=netcoreapp2.0" $configuration "publish"
}

function __target__pushmyget() {
    _build_step "Pushing packages to MyGet"
        if ($null -eq $env:MyGetApiKey) {
            Write-Host -ForegroundColor Yellow "Skipping MyGet push because environment variable 'MyGetApiKey' is not set."
            Write-Host ""
        } else {
            Get-ChildItem -Filter *.nupkg $packageOutputFolder | _nuget_push -source https://www.myget.org/F/xunit/api/v2/package -apiKey $env:MyGetApiKey
        }
}

function __target__setversion() {
    if ($buildAssemblyVersion -ne "") {
        _build_step ("Setting assembly version: '" + $buildAssemblyVersion + "'")
            Get-ChildItem -Recurse -Filter GlobalAssemblyInfo.cs | _replace -match '\("99\.99\.99\.0"\)' -replacement ('("' + $buildAssemblyVersion + '")')
    }

    if ($buildSemanticVersion -ne "") {
        _build_step ("Setting semantic version: '" + $buildSemanticVersion + "'")
            Get-ChildItem -Recurse -Filter GlobalAssemblyInfo.cs | _replace -match '\("99\.99\.99-dev"\)' -replacement ('("' + $buildSemanticVersion + '")')
            Get-ChildItem -Recurse -Filter *.nuspec | _replace -match '99\.99\.99-dev' -replacement $buildSemanticVersion
    }
}

function __target__signpackages() {
        if ($null -ne $env:SignClientSecret) {
            if ((test-path $signClientFolder) -eq $false) {
                _build_step ("Downloading SignClient " + $signClientVersion)
                    _exec ('& "' + $nugetExe + '" install SignClient -version ' + $signClientVersion + ' -SolutionDir "' + $solutionFolder + '" -Verbosity quiet -NonInteractive')
            }

            _build_step "Signing NuGet packages"
                $appPath = (join-path $signClientFolder "tools/netcoreapp2.0/SignClient.dll")
                $nupgks = Get-ChildItem (join-path $packageOutputFolder "*.nupkg") | ForEach-Object { $_.FullName }
                foreach ($nupkg in $nupgks) {
                    $cmd = '& dotnet "' + $appPath + '" sign -c "' + $signClientAppSettings + '" -r "' + $env:SignClientUser + '" -s "' + $env:SignClientSecret + '" -n "xUnit.net" -d "xUnit.net" -u "https://github.com/xunit/xunit" -i "' + $nupkg + '"'
                    $msg = $cmd.Replace($env:SignClientSecret, '[Redacted]')
                    $msg = $msg.Replace($env:SignClientUser, '[Redacted]')
                    _exec $cmd $msg
                }
        }
}

function __target__test32() {
    _build_step "Running tests: 32-bit .NET 4.x"
        $v2_assemblies = [System.String]::Join(" ", (Get-ChildItem -Recurse -Include test.xunit.*.dll | Where-Object { $_.FullName -match "bin" + $dirSeparator + $configuration + $dirSeparator + "net472" } | ForEach-Object { $_.FullName }))
        # _xunit_x86 ("test/test.xunit1/bin/" + $configuration + "/net45/test.xunit1.dll -xml artifacts/test/v1-x86.xml -html artifacts/test/v1-x86.html "                               + $nonparallelFlags)
        _xunit_x86 ($v2_assemblies                                                 + " -xml artifacts/test/v2-x86.xml -html artifacts/test/v2-x86.html -appdomains denied -serialize " + $parallelFlags)
}

function __target__test64() {
    _build_step "Running tests: 64-bit .NET 4.x"
        $v2_assemblies = [System.String]::Join(" ", (Get-ChildItem -Recurse -Include test.xunit.*.dll | Where-Object { $_.FullName -match "bin" + $dirSeparator + $configuration + $dirSeparator + "net472" } | ForEach-Object { $_.FullName }))
        _xunit_x64 ("test/test.xunit1/bin/" + $configuration + "/net45/test.xunit1.dll -xml artifacts/test/v1-x64.xml -html artifacts/test/v1-x64.html "                               + $nonparallelFlags)
        _xunit_x64 ($v2_assemblies                                                 + " -xml artifacts/test/v2-x64.xml -html artifacts/test/v2-x64.html -appdomains denied -serialize " + $parallelFlags)
}

# Dispatch

$targetFunction = (Get-ChildItem ("Function:__target_" + $target.ToLowerInvariant()) -ErrorAction SilentlyContinue)
if ($null -eq $targetFunction) {
    _fatal "Unknown target '$target'"
}

_build_step "Performing pre-build verifications"
    _require dotnet "Could not find 'dotnet'. Please ensure .NET CLI Tooling is installed."
    _verify_dotnetsdk_version "2.1.302"

    if ($needsMono) {
        _require mono "Could not find 'mono'. Please ensure Mono is installed."
    }

_mkdir $packageOutputFolder
_mkdir $testOutputFolder
& $targetFunction
