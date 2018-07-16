module XmlHelper

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Xml.Linq
open System.Xml
open FSharp.Data

open System.Collections.Immutable
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Diagnostics
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.MSBuild

type RuleSet = XmlProvider<"""
<RuleSet Name="Rules for ClassLibrary2" Description="Code analysis rules for ClassLibrary2.csproj." ToolsVersion="14.0">
  <Rules AnalyzerId="Microsoft.Analyzers.ManagedCodeAnalysis" RuleNamespace="Microsoft.Rules.Managed">
    <Rule Id="CA1001" Action="Warning" />
    <Rule Id="CA1009" Action="Warning" />
  </Rules>
  <Rules AnalyzerId="StyleCop.Analyzers" RuleNamespace="StyleCop.Analyzers">
    <Rule Id="SA1305" Action="Warning" />
    <Rule Id="SA1634" Action="None" />
  </Rules>
</RuleSet>
""" >
type ProjectFile = XmlProvider<""" 
<Project ToolsVersion="14.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System.Xml" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>
  <ItemGroup>
    <None Include="ClassLibrary2.data" />
    <None Include="ClassLibrary2.ruleset" />
    <AdditionalFiles Include="stylecop.json" />
    <AdditionalFiles Include="stylecop2.json" />    
  </ItemGroup>
  <ItemGroup>
    <Analyzer Include="..\packages\StyleCop.Analyzers.1.0.0-rc2\analyzers\dotnet\cs\StyleCop.Analyzers.CodeFixes.dll" />
    <Analyzer Include="..\packages\StyleCop.Analyzers.1.0.0-rc2\analyzers\dotnet\cs\StyleCop.Analyzers.dll" />
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project> """>

type InputXml = XmlProvider<""" 
<AnalysisInput>
  <Settings>    
      <SolutionToUse>solution.sln</SolutionToUse>
      <ExternalDiagnostics>c:\path</ExternalDiagnostics>
      <SolutionRoot>path</SolutionRoot>
      <SonarUrl>http://sonar</SonarUrl>
      <ProjectKey>key</ProjectKey>
      <BranchKey>key</BranchKey>      
      <EnableRules>true</EnableRules>   
      <UseSonarWebProfile>true</UseSonarWebProfile>   
      <AdditionalFiles>file1;file2</AdditionalFiles>
  </Settings>
</AnalysisInput>
""">

// parse command using regex
// if matched, return (command name, command value) as a tuple
let (|Command|_|) (s:string) =
    let r = new Regex(@"^(?:-{1,2}|\/)(?<command>\w+)[=:]*(?<value>.*)$",RegexOptions.IgnoreCase)
    let m = r.Match(s)
    if m.Success then 
        Some(m.Groups.["command"].Value.ToLower(), m.Groups.["value"].Value)
    else
        None

// take a sequence of argument values
// map them into a (name,value) tuple
// scan the tuple sequence and put command name into all subsequent tuples without name
// discard the initial ("","") tuple
// group tuples by name 
// convert the tuple sequence into a map of (name,value seq)
let parseArgs (args:string seq) =
    args 
    |> Seq.map (fun i -> 
                        match i with
                        | Command (n,v) -> (n,v) // command
                        | _ -> ("",i)            // data
                       )
    |> Seq.scan (fun (sn,_) (n,v) -> if n.Length>0 then (n,v) else (sn,v)) ("","")
    |> Seq.skip 1
    |> Seq.groupBy (fun (n,_) -> n)
    |> Seq.map (fun (n,s) -> (n, s |> Seq.map (fun (_,v) -> v) |> Seq.filter (fun i -> i.Length>0)))
    |> Map.ofSeq

type SonarIssue() =
    member val Rule : string = "" with get, set
    member val Line : int = 0 with get, set
    member val Component : string = "" with get, set
    member val Message : string = "" with get, set

type SonarResoureIssues(path : string) = 
    member val ResourcePath : string = path with get
    member val Issues : SonarIssue List = List.empty with get, set

let WriteToOutputFile(outputfile : string, resources : Diagnostic List) = 
    use streamfile = new StreamWriter(outputfile, true)
    
    let AppendIssueToFile(issue : Diagnostic) =
        let linecontent = sprintf "%s;%i;%s;%s" issue.Location.SourceTree.FilePath (issue.Location.GetLineSpan().StartLinePosition.Line + 1) issue.Id (issue.GetMessage())
        streamfile.WriteLine(linecontent)

    resources |> Seq.iter (fun m -> AppendIssueToFile(m))

    streamfile.Flush()



type OptionsToUse() = 
    member val Url : string = "" with get, set
    member val Root : string = "" with get, set
    member val Solution : string = "" with get, set
    member val AdditionalFiles : string [] = [||] with get, set
    member val AddionalFilesInPojectFile : string [] = [||] with get, set
    member val AnalysersFilesInProject : string [] = [||] with get, set
    member val RuleSetFile : string = "" with get, set
    member val DisableIds : Set<string> = Set.empty with get, set
    member val UseWebProfile : bool = true with get, set
    member val ProjectKey : string = "" with get, set
    member val EnableRules : bool = true with get, set
    member val ProjectPath : string = "" with get, set
    member val ExtenalDiagnostics : string [] = [||] with get, set

    member this.ParseOptions(solutionPath:string, options:InputXml.AnalysisInput) =

        this.Url <- options.Settings.SonarUrl
        this.Root <- options.Settings.SolutionRoot
        this.AdditionalFiles <- options.Settings.AdditionalFiles.Split([|';'|], StringSplitOptions.RemoveEmptyEntries)
        this.UseWebProfile <- options.Settings.UseSonarWebProfile

        let userDiagnosticsDefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VSSonarExtension", "Diagnostics", "UserDiagnostics" )
        this.ExtenalDiagnostics <- (Array.append (options.Settings.ExternalDiagnostics.Split([|';'|], StringSplitOptions.RemoveEmptyEntries)) [|userDiagnosticsDefaultPath|])

        this.Solution <- solutionPath
        this.EnableRules <- try options.Settings.EnableRules with | ex -> true


        this.ProjectKey <- 
            if options.Settings.BranchKey <> "" then
                options.Settings.ProjectKey + ":" + options.Settings.BranchKey
            else
                options.Settings.ProjectKey

    member this.PopulateProjectOptions(projectPath:string) =
        this.ProjectPath <- projectPath
        if File.Exists(this.ProjectPath) then
            let data = ProjectFile.Parse(File.ReadAllText(this.ProjectPath))
            for itemgroup in data.ItemGroups do
                try
                    for additionafile in itemgroup.AdditionalFiles do
                        this.AddionalFilesInPojectFile <- Array.append this.AddionalFilesInPojectFile [|additionafile.Include|]
                with
                | _ -> ()

                try
                    for additionafile in itemgroup.Nones do
                        if additionafile.Include.EndsWith(".ruleset") then
                            this.RuleSetFile <- 
                                if Path.IsPathRooted(additionafile.Include) then 
                                    additionafile.Include
                                else 
                                    Path.Combine(Directory.GetParent(this.ProjectPath).ToString(), additionafile.Include)
                with
                | _ -> ()

                try
                    for additionafile in itemgroup.Analyzers do
                        this.AnalysersFilesInProject <- Array.append this.AnalysersFilesInProject [|additionafile.Include|]
                with
                | _ -> ()

        if File.Exists(this.RuleSetFile) then
            let data = RuleSet.Parse(File.ReadAllText(this.RuleSetFile))
            for diag in data.Rules do
                for rule in diag.Rules do
                    if not(this.DisableIds.Contains(rule.Id)) && rule.Action.Equals("None") then
                        this.DisableIds <- this.DisableIds.Add(rule.Id)
            