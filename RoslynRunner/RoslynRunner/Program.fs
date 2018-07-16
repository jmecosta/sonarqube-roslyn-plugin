// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.
open System
open System.IO
open System.Reflection
open VSSonarPlugins
open VSSonarPlugins.Types
open SonarRestService
open MSBuildHelper

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Diagnostics
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.MSBuild

let ShowHelp () =
        Console.WriteLine ("Usage: RoslynRunner [OPTIONS]")
        Console.WriteLine ("Collects results for Sonar Analsyis using RoslynRunner")
        Console.WriteLine ()
        Console.WriteLine ("Options:")
        Console.WriteLine ("    /I|/i:<input xml>")
        Console.WriteLine ("    /O|/o:<output xml file>")
        Console.WriteLine ("    /U|/u:<username>")
        Console.WriteLine ("    /P|/p:<password>")
        Console.WriteLine ("    /delete-all-rules")
    
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
    
    if arguments.ContainsKey("h") then
        ShowHelp()
    elif arguments.ContainsKey("i") then
        if not(arguments.ContainsKey("o")) then
            Console.WriteLine ("    Mission /O")
            ShowHelp()
        else
            try
                let input = arguments.["i"] |> Seq.head
                let output = arguments.["o"] |> Seq.head

                if File.Exists(output) then
                    File.Delete(output)

                let username = try arguments.["u"] |> Seq.head with | ex -> "admin"
                let userpassword = try arguments.["p"] |> Seq.head with | ex -> if username = "admin" then "admin" else ""

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
                let rest = new SonarRestService(new JsonSonarConnector()) :> ISonarRestService
                let token = SonarHelpers.GetConnectionToken(rest, options.Url, username, userpassword)
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
                    printf "[RoslynRunner] : Populate Diagnostics\r\n"
                    let diagnosticRefs = GetDiagnostics(options.Solution, options.ExtenalDiagnostics, options.Root)
                    printf "[RoslynRunner] : Sync Rules in Server\r\n"
                    let diagnostics = SonarHelpers.SyncRulesInServer(diagnosticRefs, options.Root, rest, token, options.EnableRules, options.ProjectKey, true)
                    let profiles = 
                        if options.UseWebProfile then
                            printf "[RoslynRunner] : Use Web Profile : Delete Complete Profile\r\n"
                            SonarHelpers.DeleteCompleteProfile(token, rest, options.ProjectKey)
                            printf "[RoslynRunner] : Get Profiles\r\n"
                            SonarHelpers.GetProfilesFromServer(options.ProjectKey, rest, token, false)
                        else
                            // read rule set and enable all rules that might be disabled
                            printf "[RoslynRunner] : Create and Assign Profile in Server\r\n"
                            SonarHelpers.CreateAndAssignProfileInServer(options.ProjectKey, rest, token, diagnostics)

                    for project in solutiondata.Projects do
                        printf "[RoslynRunner] : Analyse: %s \r\n" project.Value.Path
                        options.PopulateProjectOptions(project.Value.Path)
                        if diagnostics.Count = 0 then
                            printf "[RoslynRunner] : No diagnostics configured or found : see https://sites.google.com/site/jmecsoftware/ for more information\r\n"
                        else
                            for dll in diagnostics do
                                if dll.Value.Length <> 0 then
                                    printf "[RoslynRunner] : Run analyzers in : %s\r\n" dll.Key
                                    let resourceswithissues = RoslynHelper.RunAnalysis(profiles, dll.Value, options)
                                    resourceswithissues |> Seq.iter (fun x -> diagnostiResults <- diagnostiResults @ [x])

                    XmlHelper.WriteToOutputFile(output, diagnostiResults)
            with
            | ex -> printf "    Failed: %A" ex
        ()
    else
        ShowHelp()

    0
