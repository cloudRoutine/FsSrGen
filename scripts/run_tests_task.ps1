# exists the script if the preceeding command failed
function check-last { if(-not $?){ exit 1 }}

$scriptDir = split-path $script:MyInvocation.MyCommand.Path
$repoDir = split-path -parent $scriptdir
$testDir = "$repoDir\test\use-fssrgen-as-msbuild-task"

# restore testproject and tools from package 
dotnet restore $testDir   -f "$repoDir\bin\packages\"
check-last  

# run tool
msbuild "$testdir\FsSrGenAsMsbuildTask.msbuild" /verbosity:detailed 
check-last  

$stored = $pwd
# restore test project
#dotnet restore $testDir
#check-last  
cd $testDir

# build
dotnet -v build 
check-last  

# run tests netcoreapp 1.0
dotnet run  --framework netcoreapp1.0 -- --verbose
check-last  

# run tests net45
.\bin\Debug\net45\win7-x64\use-fssrgen-as-msbuild-task.exe --verbose
check-last  

cd $stored

exit 0
