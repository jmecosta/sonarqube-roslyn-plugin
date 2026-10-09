// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.
open System
open System.IO
open System.Reflection
open SonarRestService
open MSBuildHelper

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Diagnostics
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.MSBuild
open SonarRestServiceImpl
open SonarRestService.Types

let ShowHelp () =
        Console.WriteLine ("Usage: RoslynRunner [OPTIONS]")
        Console.WriteLine ("Collects results for Sonar Analsyis using RoslynRunner")
        Console.WriteLine ()
        Console.WriteLine ("Options:")
        Console.WriteLine ("    /I|/i:<input xml>")
        Console.WriteLine ("    /O|/o:<output xml file>")
        Console.WriteLine ("    /T|/t:<token>")
        Console.WriteLine ("    /P|/p:<projects analysed at the same time, default: half the processors> (or ROSLYN_RUNNER_PARALLELISM)")
        Console.WriteLine ("    /B|/b:<projects per solution load, default: 32> (or ROSLYN_RUNNER_BATCH)")
        Console.WriteLine ("    /delete-all-rules")
        Console.WriteLine ("    /createrules /d:<dll-or-folder> /url:<sonar url> /t:<token>")
    
let GetDiagnostics(solution:string, externalAnalysers:string [], root : string) =
    let mutable paths : Map<string, string> = Map.empty
    let mutable pathstoreturn = List.Empty
    use workspace = MSBuildWorkspace.Create()
    let solutiodn = workspace.OpenSolutionAsync(solution).Result

    for digPathFolder in externalAnalysers do
        if Directory.Exists(digPathFolder) then
            let filesInFolder = Directory.GetFiles(digPathFolder)

            for dig in filesInFolder do
                let name = Path.GetFileNameWithoutExtension(dig)
                let filepath = 
                    if Path.IsPathRooted(dig) then
                        dig
                    else
                        Path.Combine(root, dig)

                if not(paths.ContainsKey(name)) && not(name.Contains("SonarLint")) && not(name.Contains("SonarAnalyser")) then
                    paths <- paths.Add(name, filepath)
                    pathstoreturn <- pathstoreturn @ [filepath]

    for project in solutiodn.Projects do
        let compilation = project.AnalyzerReferences
        for analyser in compilation do
            let name = Path.GetFileNameWithoutExtension(analyser.FullPath)
            if not(paths.ContainsKey(name)) && not(name.Contains("SonarLint")) && not(name.Contains("SonarAnalyser")) then
                paths <- paths.Add(name, analyser.FullPath)
                pathstoreturn <- pathstoreturn @ [analyser.FullPath]

    List.toArray pathstoreturn


[<EntryPoint>]
let main argv = 
    let arguments = XmlHelper.parseArgs(argv)
    let mutable exitCode = 0
    
    if arguments.ContainsKey("h") then
        ShowHelp()
    elif arguments.ContainsKey("createrules") then
        // Stand-alone, user-run utility: register rules from a diagnostic DLL (or folder)
        // into the roslyn-cs / roslyn-vbnet repositories. Requires a token with admin rights.
        // Activation is left to the user in the Sonar UI. The scanner never writes rules.
        let dpath = try arguments.["d"] |> Seq.head with | _ -> ""
        let url = try arguments.["url"] |> Seq.head with | _ -> ""
        if dpath = "" || url = "" then
            Console.WriteLine ("    /createrules requires /d:<dll-or-folder> and /url:<sonar url>")
            ShowHelp()
            exitCode <- 1
        else
            try
                let token = try arguments.["t"] |> Seq.head with | _ -> "xxxx"
                let rest = new SonarService(new JsonSonarConnector()) :> ISonarRestService
                let conn = SonarHelpers.GetConnectionToken(rest, url, token, "")
                SonarHelpers.CreateRulesInRepository(dpath, rest, conn)
            with
            | ex ->
                eprintfn "    Failed: %A" ex
                exitCode <- 1
    elif arguments.ContainsKey("i") then
        if not(arguments.ContainsKey("o")) then
            Console.WriteLine ("    Mission /O")
            ShowHelp()
            exitCode <- 1
        else
            try
                let input = arguments.["i"] |> Seq.head
                let output = arguments.["o"] |> Seq.head

                if File.Exists(output) then
                    File.Delete(output)

                let token = try arguments.["t"] |> Seq.head with | ex -> "xxxx"

                let optionsInput = XmlHelper.InputXml.Parse(File.ReadAllText(input))

                let solutionPath =
                    if Path.IsPathRooted(optionsInput.Settings.SolutionToUse) then
                        optionsInput.Settings.SolutionToUse
                    else
                        Path.Combine(optionsInput.Settings.SolutionRoot, optionsInput.Settings.SolutionToUse)

                let solutiondata = MSBuildHelper.CreateSolutionData(solutionPath)
                let mutable diagnostiResults : Diagnostic list = List.Empty
                let options = new XmlHelper.OptionsToUse()
                options.ParseOptions(solutionPath, optionsInput)
                let rest = new SonarService(new JsonSonarConnector()) :> ISonarRestService
                let token = SonarHelpers.GetConnectionToken(rest, options.Url, token, "")
                if arguments.ContainsKey("deleteallrules") then
                    let profiles = SonarHelpers.GetProfilesFromServer(options.ProjectKey, rest, token, true)
                    if profiles.ContainsKey("cs") then SonarHelpers.DeleteRoslynRulesInProfiles(rest, token, profiles.["cs"])
                    if profiles.ContainsKey("vbnet") then SonarHelpers.DeleteRoslynRulesInProfiles(rest, token, profiles.["vbnet"])
                elif arguments.ContainsKey("deletealldiagnosticsfromserver") then
                    let diagnosticRefs = GetDiagnostics(options.Solution, options.ExtenalDiagnostics, options.Root)
                    let diagnostics = SonarHelpers.SyncRulesInServer(diagnosticRefs, options.Root, rest, token, options.EnableRules, options.ProjectKey, true)

                    for diagnostic in diagnostics do
                        for diag in diagnostic.Value do
                            for sup in diag.Analyser.SupportedDiagnostics do
                                let rule = Rule()
                                rule.Key <- "roslyn-cs:" + sup.Id
                                let result = rest.DeleteRule(token, rule)
                                printf "result: %A" result
                else
                    printf "[RoslynRunner] : ProjectKey: %s \r\n" options.ProjectKey
                    printf "[RoslynRunner] : Load Diagnostics\r\n"
                    // Scanner is read-only: it loads the analyzers locally and reads the
                    // Sonar web profile to decide which rules are active. It never creates,
                    // copies, activates or deletes rules/profiles in the server. Use the
                    // /createrules utility (run by an admin) to register rules.
                    let diagnostics = SonarHelpers.LoadDiagnostics(options.ExtenalDiagnostics, options.Root)
                    printf "[RoslynRunner] : Get Profiles (read-only)\r\n"
                    let profiles = SonarHelpers.GetProfilesFromServer(options.ProjectKey, rest, token, false)

                    if diagnostics.Count = 0 then
                        printf "[RoslynRunner] : No diagnostics configured or found : see https://sites.google.com/site/jmecsoftware/ for more information\r\n"
                    else
                        let analyzers =
                            diagnostics
                            |> Map.toList
                            |> List.filter (fun (_, diags) -> diags.Length <> 0)
                            |> List.map (fun (dll, diags) -> dll, RoslynHelper.PrepareAnalyzers(profiles, diags))
                        // the Sonar plugin passes only /i /t /o: the environment can set these too
                        let setting (key : string) (envName : string) (fallback : int) =
                            let value =
                                if arguments.ContainsKey(key) then arguments.[key] |> Seq.head
                                else Environment.GetEnvironmentVariable(envName)
                            match Int32.TryParse(value) with
                            | true, n when n > 0 -> n
                            | _ -> fallback
                        let parallelism = setting "p" "ROSLYN_RUNNER_PARALLELISM" (max 1 (Environment.ProcessorCount / 2))
                        let batchSize = setting "b" "ROSLYN_RUNNER_BATCH" 32
                        let projectPaths = solutiondata.Projects.Values |> Seq.map (fun p -> p.Path) |> Seq.toList
                        printf "[RoslynRunner] : Analyse %i projects: %i at a time, solution loaded once per %i projects\r\n" projectPaths.Length parallelism batchSize
                        // the analysis blocks on Roslyn tasks: enough pool threads so the parallel projects do not starve them
                        let workers, ports = System.Threading.ThreadPool.GetMinThreads()
                        System.Threading.ThreadPool.SetMinThreads(max workers (parallelism * 4), ports) |> ignore

                        let analyseBatch (batch : string list) =
                            // one workspace per batch, not per project; disposing it bounds the compilations kept in memory
                            use workspace = MSBuildWorkspace.Create()
                            let solution = workspace.OpenSolutionAsync(solutionPath).Result
                            batch
                            |> List.map (fun projectPath -> async {
                                printf "[RoslynRunner] : Analyse: %s \r\n" projectPath
                                let projectOptions = new XmlHelper.OptionsToUse()
                                projectOptions.ParseOptions(solutionPath, optionsInput)
                                projectOptions.PopulateProjectOptions(projectPath)
                                return
                                    [ for dll, prepared in analyzers do
                                        printf "[RoslynRunner] : Run analyzers in : %s (%s)\r\n" dll (Path.GetFileName(projectPath))
                                        yield! RoslynHelper.RunAnalysis(solution, profiles, prepared, projectOptions) ] })
                            |> fun jobs -> Async.Parallel(jobs, maxDegreeOfParallelism = parallelism)
                            |> Async.RunSynchronously
                            |> List.concat

                        diagnostiResults <- projectPaths |> List.chunkBySize batchSize |> List.collect analyseBatch

                    XmlHelper.WriteToOutputFile(output, diagnostiResults)
            with
            | ex ->
                eprintfn "    Failed: %A" ex
                exitCode <- 1
        ()
    else
        ShowHelp()

    exitCode
