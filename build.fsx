System.IO.Directory.SetCurrentDirectory __SOURCE_DIRECTORY__
// FAKE build script 
// --------------------------------------------------------------------------------------

#r "packages/FAKE/tools/FakeLib.dll"
open System
open Fake.AppVeyor
open Fake
open Fake.Git
open Fake.ReleaseNotesHelper
open Fake.UserInputHelper
open Fake.AssemblyInfoFile

// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let project = "FsSrGen"
let authors = ["Don Syme";"Enrico Sada";"Jared Hester"]

// let gitOwner = "fsprojects"
let gitOwner = "cloudRoutine"
let gitHome = "https://github.com/" + gitOwner

let gitName = "FsSrGen"
// let gitRaw = environVarOrDefault "gitRaw" "https://raw.githubusercontent.com/fsprojects"
let gitRaw = environVarOrDefault "gitRaw" "https://raw.githubusercontent.com/cloudRoutine"

// The rest of the code is standard F# build script 
// --------------------------------------------------------------------------------------


Target "Clean" (fun _ ->    
    !! "src/**/bin"
    ++ "src/**/obj"
    ++ "test/**/bin"
    ++ "test/**/obj"
    ++ "bin"
    |> CleanDirs        
)


let assertExitCodeZero x = if x = 0 then () else failwithf "Command failed with exit code %i" x

let runCmdIn workDir exe = Printf.ksprintf (fun args -> Shell.Exec(exe, args, workDir) |> assertExitCodeZero)

/// Execute a dotnet cli command
let dotnet workDir = runCmdIn workDir "dotnet"


let root = __SOURCE_DIRECTORY__
let srcDir = root</>"src"
let testDir = root</>"test"
let genDir = srcDir</>"fssrgen"
let dotnetDir = srcDir</>"dotnet-fssrgen"
let buildTaskDir = srcDir</>"FSharp.SRGen.Build.Tasks"
let pkgOutputDir = root</>"bin"</>"packages" 


Target "CreatePackages" (fun _ ->

    dotnet srcDir "restore"
    // Build FsSrGen nupkg
    dotnet genDir "restore" 
    dotnet genDir "pack -c Release --output %s" pkgOutputDir
    // Build dotnet-fssrgen nupkg    
    dotnet dotnetDir "restore"  
    dotnet dotnetDir "pack -c Release --output %s" pkgOutputDir 
    // Build FSharp.SRGen.Build.Tasks nupkg 
    dotnet buildTaskDir "restore"  
    dotnet buildTaskDir "pack -c Release --output %s" pkgOutputDir
            
)

// Run Tests for the dotnet cli tool
// --------------------------------------------------------------------------------------

let cliProjName = "use-dotnet-fssrgen-as-tool"
let testToolDir = root</>"test"</>cliProjName

Target "RunTestsTool" (fun _ ->
    dotnet testDir "restore"
    dotnet testToolDir "restore"
    dotnet testToolDir "fssrgen %s %s %s %s" 
        (testToolDir</>"FSComp.txt") (testToolDir</>"FSComp.fs") (testToolDir</>"FSComp.resx") cliProjName
    dotnet testToolDir "build"
    dotnet testToolDir "test"
            
)

// Run Tests for the msbuild task
// --------------------------------------------------------------------------------------
 
let testTaskDir =  root</>"test"</>"use-fssrgen-as-msbuild-task"
let msbuild workDir = runCmdIn workDir "msbuild"
let nuget workDir = runCmdIn workDir ("packages"</>"Nuget.CommandLine"</>"tools"</>"nuget.exe")
let fssrgenTaskExe workDir = runCmdIn workDir (root</>"bin"</>"Debug"</>"net46"</>"win7-x64"</>"use-fssrgen-as-msbuild-task.exe")

Target "RunTestsTask" (fun _ ->
    nuget testTaskDir "restore"
    dotnet testTaskDir "restore"   
    msbuild testTaskDir "%s" (testTaskDir</>"FsSrGenAsMsbuildTask.msbuild /verbosity:detailed")
    dotnet testTaskDir "-v build"
    dotnet testTaskDir "--framework netcoreapp1.0 -- --verbose"
    fssrgenTaskExe testToolDir "--verbose"
    
)


Target "PublishNuGet" (fun _ ->
    Paket.Push (fun p -> 
        let apikey =
            match getBuildParam "nuget-apikey" with
            | s when not (String.IsNullOrWhiteSpace s) -> s
            | _ -> getUserInput "Nuget API Key: "
        { p with
            ApiKey = apikey
            WorkingDir = pkgOutputDir }) 
)


#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit


Target "GitHubRelease" (fun _ ->
    let user =
        match getBuildParam "github-user" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserInput "GitHub Username: "
    let pw =
        match getBuildParam "github-pw" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "GitHub Password: "
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]


    
    let releaseFile pkgSuffix =
        __SOURCE_DIRECTORY__ </> (sprintf "RELEASE_NOTES_%s.md" pkgSuffix)
    
    // Read release notes & version info from RELEASE_NOTES.md
    let makeNotes pkgSuffix : ReleaseNotes = 
        LoadReleaseNotes (releaseFile  pkgSuffix)    
    
    let releasePkg pkgName releaseSuffix release =

        Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
        Branches.pushBranch "" remote (Information.getBranchName "")

        Branches.tag "" release.NugetVersion
        Branches.pushTag "" remote release.NugetVersion
        
        // Give each nupkg line its own tag
        Branches.tag "" pkgName
        Branches.pushTag "" remote pkgName
        
        createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
    
    // release on github
    
    let uploadRelease client pkgName pkgSuffix =
        let releaseFileName = releaseFile pkgSuffix
        StageFile "" releaseFileName |> ignore

        let notes = makeNotes pkgSuffix       
        
        client 
        |> releasePkg pkgName pkgSuffix notes 
        |> uploadFile (pkgOutputDir</>(pkgName + notes.NugetVersion + ".nupkg"))
        |> releaseDraft    
        |> Async.RunSynchronously
        
    let client = createClient user pw

    uploadRelease client "fssrgen" "FSSRGEN"    
    uploadRelease client "dotnet-fssrgen" "DOTNET_FSSRGEN"    
    uploadRelease client "FSharp.SRGen.Build.Tasks" "FSHARP_SRGEN_BUILDTASKS"    
)


"Clean"
    ==> "CreatePackages"


Target "RunTests" DoNothing
"CreatePackages"
    =?> ("RunTestsTool",isWindows)
    =?> ("RunTestsTask",isWindows)
    ==> "RunTests"


Target "Release" DoNothing
"CreatePackages"
    ==> "RunTests"
    ?=> "GitHubRelease"
    ==> "PublishNuGet"
    ==> "Release"


RunTargetOrDefault "RunTests"