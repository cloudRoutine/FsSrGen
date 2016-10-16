#make path absolute
$repoDir = Split-Path -parent (Split-Path -parent $PSCommandPath)

Push-Location

cd "$repoDir\test\use-fssrgen-as-msbuild-task"


# restore package

& "$repoDir\.nuget\NuGet.exe" restore .\packages.config -PackagesDirectory packages -Source "$repoDir\bin\packages"
if (-not $?) {
	exit 1
}

# run tool
$testProjDir = $pwd
msbuild FsSrGenAsMsbuildTask.msbuild /verbosity:detailed 
if (-not $?) {
	exit 1
}


# restore test project

dotnet restore
if (-not $?) {
	exit 1
}


# build

dotnet --verbose build
if (-not $?) {
	exit 1
}

# run tests netcoreapp 1.0

dotnet run --framework netcoreapp1.0 -- --verbose
if (-not $?) {
	exit 1
}

# run tests net45

.\bin\Debug\net45\win7-x64\use-fssrgen-as-msbuild-task.exe --verbose
if (-not $?) {
	exit 1
}

Pop-Location
exit 0
