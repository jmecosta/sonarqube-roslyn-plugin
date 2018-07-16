module RoslynHelper

open Microsoft.CodeAnalysis.Diagnostics

open VSSonarPlugins
open VSSonarPlugins.Types
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


type AnalyzerAdditionalFile(path : string) =
    inherit AdditionalText()

    override this.Path : string = path

    override this.GetText(cancellationToken : CancellationToken) =
        SourceText.From(File.ReadAllText(path))

type RosDiag() = 
    member val Analyser : DiagnosticAnalyzer = null with get, set 
    member val Languages : string [] = [||] with get, set

let LoadDiagnosticsFromPath(path : string) = 
    
    let runningPath = Directory.GetParent(Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", "")).ToString()

    AppDomain.CurrentDomain.add_AssemblyResolve(fun _ args ->
            
        let name = System.Reflection.AssemblyName(args.Name)
        
        let path = Path.Combine(runningPath, name.Name + ".dll")

        if name.Name = "System.Windows.Interactivity" || name.Name = "FSharp.Core.resources" || name.Name.EndsWith(".resources") then
            null
        else
            printf "Request to load %s %s\n\r" args.Name (path)
            
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
    let analyserMain = compilationWithOptions.WithAnalyzers(builder.ToImmutableArray(), optionsWithAdditionalFiles, (new CancellationTokenSource()).Token)

    analyserMain.GetAnalyzerDiagnosticsAsync().Result

let RunAnalysis(profiles : System.Collections.Generic.Dictionary<string, Profile>, roslynCheckers : RosDiag List, options : XmlHelper.OptionsToUse) =
    let mutable issuestoret = List.Empty

    try
        use workspace = MSBuildWorkspace.Create()
        let solution = workspace.OpenSolutionAsync(options.Solution).Result
        let builder, ids = UpdateDiagnostics(profiles, roslynCheckers)
               
        let csharpDiags =
            let mutable diagret = List.Empty
            let diags = builder |> List.filter ( fun c -> c.Languages |> Seq.contains("C#"))
            for diag in diags do
                diagret <- diagret @ [diag.Analyser]
            diagret

        let vbnetDiags =
            let mutable diagret = List.Empty
            let diags = builder |> List.filter ( fun c -> c.Languages |> Seq.contains("VB"))
            for diag in diags do
                diagret <- diagret @ [diag.Analyser]
            diagret

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
            printf "[RoslynRunner] : No diagnostics enabled, skip execution.\n\r"

    with
    | ex -> 
        printf "Failed to run diagnostics %s \r\n %s\n\r" ex.Message ex.StackTrace

    printf "[RoslynRunner] : Found %i issues\r\n" issuestoret.Length
    issuestoret