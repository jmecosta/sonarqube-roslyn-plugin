module RoslynHelper

open Microsoft.CodeAnalysis.Diagnostics

open System
open System.IO
open System.Reflection
open System.Collections.Immutable
open System.Threading

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Diagnostics
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.MSBuild
open Microsoft.CodeAnalysis.Text
open SonarRestService.Types

let private currentProject = AsyncLocal<string>()

type AnalyzerAdditionalFile(path : string) =
    inherit AdditionalText()

    override this.Path : string = path

    override this.GetText(cancellationToken : CancellationToken) =
        SourceText.From(File.ReadAllText(path))

type RosDiag() = 
    member val Analyser : DiagnosticAnalyzer = null with get, set 
    member val Languages : string [] = [||] with get, set

let LoadDiagnosticsFromPath(path : string) = 
    
    let runningPath = Directory.GetParent(Assembly.GetExecutingAssembly().Location).ToString()

    AppDomain.CurrentDomain.add_AssemblyResolve(fun _ args ->
            
        let name = System.Reflection.AssemblyName(args.Name)
        
        let path = Path.Combine(runningPath, name.Name + ".dll")

        if name.Name = "System.Windows.Interactivity" || name.Name = "FSharp.Core.resources" || name.Name.EndsWith(".resources") then
            null
        else
            let projectContext =
                if String.IsNullOrEmpty(currentProject.Value) then "shared initialization / solution loading"
                else currentProject.Value
            let requestingAssembly =
                if isNull args.RequestingAssembly then "unknown"
                else args.RequestingAssembly.FullName
            Console.WriteLine("[RoslynRunner] : Project: {0} | Request to load: {1} | Requested by: {2} | Candidate: {3}",
                              projectContext, args.Name, requestingAssembly, path)
            
            let existingAssembly = 
                System.AppDomain.CurrentDomain.GetAssemblies()
                |> Seq.tryFind(fun a -> System.Reflection.AssemblyName.ReferenceMatchesDefinition(name, a.GetName()))
            match existingAssembly with
            | Some a -> a
            | None -> 
                let path = Path.Combine(runningPath, name.Name + ".dll")
                if File.Exists(path) then 
                    let inFileAssembly = Assembly.LoadFile(path)
                    inFileAssembly
                else
                    let folder = Path.GetDirectoryName(path)
                    let path = Path.Combine(folder, name.Name + ".dll")
                    if File.Exists(path) then
                        let inFileAssembly = Assembly.LoadFile(path)
                        inFileAssembly
                    else
                        null
    )

    let assembly = Assembly.LoadFrom(path)

    let mutable analyzers = List.Empty

    try
        for elem in assembly.GetTypes() do
            if elem.IsSubclassOf(typeof<DiagnosticAnalyzer>) && not(elem.IsAbstract) then
                try
                    let diag = Activator.CreateInstance(elem) :?> DiagnosticAnalyzer
                    let attributes = elem.GetCustomAttributes()

                    let attribute = Attribute.GetCustomAttribute(elem, typeof<DiagnosticAnalyzerAttribute>) :?> DiagnosticAnalyzerAttribute
                    analyzers <- analyzers @ [new RosDiag(Analyser = diag, Languages = attribute.Languages)]
                with
                | ex -> ()
    with
    | ex -> 
        let ex = ex :?> ReflectionTypeLoadException
        for t in ex.Types do
            printf "Failed to loaded %s %s\n\r" (t.ToString()) (ex.Types.Length.ToString())
                
    printf "[RoslynRunner] Loaded %i diagnostic analyzers from %s\n\r" analyzers.Length (path)
    analyzers    


let UpdateDiagnostics(externlProfileIn : System.Collections.Generic.Dictionary<string, Profile>, checksRoslyn : RosDiag List) =
    let mutable builder = List.empty
    let mutable ids = List.empty
    for check in checksRoslyn do
        try
            let mutable checkadded = false
            for diagnostic in check.Analyser.SupportedDiagnostics do
                if not(checkadded) then                    
                    for lang in check.Languages do
                        let language, repo = 
                            if lang.Equals("C#") then
                                "cs", "roslyn-cs"
                            else
                                "vbnet", "roslyn-vbnet"
                            
                        let id = repo + ":" + diagnostic.Id
                        let rule = externlProfileIn.[language].GetRule(id)
                        if rule <> null then
                            checkadded <- true
                            builder <- builder @ [check]
                            ids <- ids @ [new System.Collections.Generic.KeyValuePair<string, ReportDiagnostic>(diagnostic.Id, ReportDiagnostic.Warn)]


                            if rule.Params.Count <> 0 then
                                let fields = check.GetType().GetProperties()
                                for field in fields do
                                    let attributes = field.GetCustomAttributes().ToImmutableArray()
                                    if attributes.Length = 1 &&
                                        attributes.[0].TypeId.ToString().EndsWith("Common.RuleParameterAttribute") then
                                        try
                                            let typeOfField = field.PropertyType
                                            let typeOfFiledName = field.PropertyType.Name
                                            if typeOfFiledName.Equals("IImmutableSet`1") then
                                                let elems = rule.Params.[0].DefaultValue.Replace("\"", "").Split(',').ToImmutableHashSet()
                                                field.SetValue(check, elems)
                                            else
                                                let changedValue = Convert.ChangeType(rule.Params.[0].DefaultValue.Replace("\"", ""), typeOfField)
                                                field.SetValue(check, changedValue)

                                            let value = field.GetValue(check)
                                            System.Diagnostics.Debug.WriteLine("Applied Rule Parameter: " + diagnostic.Id + " = " + rule.Params.[0].DefaultValue)
                                        with
                                        | ex -> 
                                            System.Diagnostics.Debug.WriteLine("Applied Rule Parameter: " + diagnostic.Id + " = " + rule.Params.[0].DefaultValue)
                                ()
        with
        | ex -> System.Diagnostics.Debug.WriteLine("Cannot Add Check Failed: " + check.ToString() + " : " +  ex.Message)

    System.Diagnostics.Debug.WriteLine("Checks Enabled: " + checksRoslyn.Length.ToString())

    builder, ids


let runRoslynOnCompilationUnit(compilation : Compilation, ids, builder : DiagnosticAnalyzer list, additionaldocs : System.Collections.Generic.IEnumerable<TextDocument>, sonarAdditionalDocument : string [], userWebProfile : bool) =
        
    let mutable docs = List.Empty
    if userWebProfile then
        for doc in sonarAdditionalDocument do
            docs <- docs @ [new AnalyzerAdditionalFile(doc) :> AdditionalText]
    else
        for doc in additionaldocs do
            docs <- docs @ [new AnalyzerAdditionalFile(doc.FilePath) :> AdditionalText]
        
    let optionsWithAdditionalFiles = new AnalyzerOptions((List.toArray docs).ToImmutableArray())

    let options = 
        (if compilation.Language.Equals(LanguageNames.CSharp) then
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            else
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        ).WithSpecificDiagnosticOptions(ids)
        
    let compilationWithOptions = compilation.WithOptions(options)
    let analyserMain = compilationWithOptions.WithAnalyzers(builder.ToImmutableArray(), optionsWithAdditionalFiles)

    analyserMain.GetAnalyzerDiagnosticsAsync().Result

type PreparedAnalyzers =
    { CSharp : DiagnosticAnalyzer list
      VbNet : DiagnosticAnalyzer list
      Ids : (string * ReportDiagnostic) list }

// UpdateDiagnostics sets rule parameters on the shared analyzer instances: run it once, before projects are analysed in parallel
let PrepareAnalyzers(profiles : System.Collections.Generic.Dictionary<string, Profile>, roslynCheckers : RosDiag List) =
    let builder, ids = UpdateDiagnostics(profiles, roslynCheckers)
    let analyzersFor (language : string) =
        builder |> List.filter (fun c -> c.Languages |> Seq.contains language) |> List.map (fun c -> c.Analyser)
    { CSharp = analyzersFor "C#"
      VbNet = analyzersFor "VB"
      Ids = ids |> List.map (fun kv -> kv.Key, kv.Value) }

let RunAnalysis(solution : Solution, profiles : System.Collections.Generic.Dictionary<string, Profile>, prepared : PreparedAnalyzers, options : XmlHelper.OptionsToUse) =
    let previousProject = currentProject.Value
    currentProject.Value <- options.ProjectPath
    use projectScope =
        { new IDisposable with
            member _.Dispose() = currentProject.Value <- previousProject }
    let mutable issuestoret = List.Empty

    try
        let ids = prepared.Ids |> List.map (fun (k, v) -> new System.Collections.Generic.KeyValuePair<string, ReportDiagnostic>(k, v))
        let csharpDiags = prepared.CSharp
        let vbnetDiags = prepared.VbNet

        if ids.Length > 0 then
            for project in solution.Projects do
                if options.ProjectPath = "" || project.FilePath.ToLower().Equals(options.ProjectPath.ToLower()) then
                    let compilation = project.GetCompilationAsync().Result
                    let specificDiagnosticsOptions = project.CompilationOptions.SpecificDiagnosticOptions
                    if project.Language.ToString().Equals("C#") then
                        let result = runRoslynOnCompilationUnit(compilation, ids.ToImmutableDictionary(), csharpDiags, project.AdditionalDocuments, options.AdditionalFiles, options.UseWebProfile)
                        for issue in result do
                            let add = 
                                if not(options.UseWebProfile) then
                                    for dig in specificDiagnosticsOptions do
                                        printf "diagnostic %A %s\r\n" dig.Value dig.Key
                                    if specificDiagnosticsOptions.Count <> 0 then
                                        try
                                            not(specificDiagnosticsOptions.[issue.Id].Equals(ReportDiagnostic.Suppress))
                                        with
                                        | ex -> printf "not found diagnostic %s\r\n" issue.Id
                                                false
                                    else
                                        if options.RuleSetFile <> "" then
                                            if options.DisableIds.Contains(issue.Id) then
                                                false
                                            else
                                                true
                                        else
                                            false
                                else
                                    let rule = profiles.["cs"].GetRule("roslyn-cs:" + issue.Id)
                                    if rule <> null then
                                        true
                                    else
                                        false
                            if add then
                                issuestoret <- issuestoret @ [issue]
                    else
                        let result = runRoslynOnCompilationUnit(compilation, ids.ToImmutableDictionary(), vbnetDiags, project.AdditionalDocuments, options.AdditionalFiles, options.UseWebProfile)

                        for issue in result do
                            let add = 
                                if not(options.UseWebProfile) then
                                    for dig in specificDiagnosticsOptions do
                                        printf "diagnostic %A %s\r\n" dig.Value dig.Key
                                    if specificDiagnosticsOptions.Count <> 0 then
                                        try
                                            not(specificDiagnosticsOptions.[issue.Id].Equals(ReportDiagnostic.Suppress))
                                        with
                                        | ex -> printf "not found diagnostic %s\r\n" issue.Id
                                                false
                                    else
                                        if options.RuleSetFile <> "" then
                                            if options.DisableIds.Contains(issue.Id) then
                                                false
                                            else
                                                true
                                        else
                                            false
                                else
                                    let rule = profiles.["vbnet"].GetRule("roslyn-vbnet:" + issue.Id)
                                    if rule <> null then
                                        true
                                    else
                                        false
                            if add then
                                issuestoret <- issuestoret @ [issue]

        else
            Console.WriteLine("[RoslynRunner] : Project: {0} | No diagnostics enabled, skip execution.", options.ProjectPath)

    with
    | ex -> 
        Console.Error.WriteLine("[RoslynRunner] : Project: {0} | Analysis failed: {1}", options.ProjectPath, ex.Message)
        Console.Error.WriteLine(ex.StackTrace)
        reraise()

    Console.WriteLine("[RoslynRunner] : Project: {0} | Found {1} issues", options.ProjectPath, issuestoret.Length)
    issuestoret