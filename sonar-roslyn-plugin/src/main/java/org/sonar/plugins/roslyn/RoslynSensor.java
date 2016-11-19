/*
 * Sonar Roslyn Plugin :: Core
 * Copyright (C) 2016 jmecsoftware.com
 * sonarqube@googlegroups.com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02
 */
package org.sonar.plugins.roslyn;

import com.google.common.base.Charsets;
import com.google.common.base.Throwables;
import com.google.common.collect.ImmutableMap;
import com.google.common.io.Files;
import java.io.BufferedReader;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.sonar.api.batch.DependedUpon;
import org.sonar.api.batch.Sensor;
import org.sonar.api.batch.SensorContext;
import org.sonar.api.batch.fs.FileSystem;
import org.sonar.api.batch.fs.InputFile;
import org.sonar.api.component.ResourcePerspectives;
import org.sonar.api.config.Settings;
import org.sonar.api.issue.Issuable;
import org.sonar.api.resources.Project;
import org.sonar.api.rule.RuleKey;
import org.sonar.api.utils.command.Command;
import org.sonar.api.utils.command.CommandExecutor;
import org.sonar.api.utils.command.StreamConsumer;
import org.sonar.api.batch.bootstrap.ProjectReactor;

import java.io.File;
import java.io.FileNotFoundException;
import java.io.FileReader;
import java.io.FilenameFilter;
import java.io.IOException;
import java.util.Map;

@DependedUpon("RoslynRunnerExtractor")
public class RoslynSensor implements Sensor {

  private static final Logger LOG = LoggerFactory.getLogger(RoslynSensor.class);

  private final Settings settings;
  private final RoslynRunnerExtractor extractor;
  private final FileSystem fs;
  private final ResourcePerspectives perspectives;
  private final ProjectReactor reactor;

  public RoslynSensor(Settings settings, RoslynRunnerExtractor extractor, FileSystem fs, ResourcePerspectives perspectives, 
    ProjectReactor reactor) {
    this.reactor = reactor;
    this.settings = settings;
    this.extractor = extractor;
    this.fs = fs;
    this.perspectives = perspectives;
  }

  public File[] finder(File dir, final String extension){

      return dir.listFiles(new FilenameFilter() { 
               @Override
               public boolean accept(File dir, String filename)
                    { 
                      return filename.endsWith(extension);
                    }
      } );
  }

  @Override
  public boolean shouldExecuteOnProject(Project project) {    
    return fs.files(fs.predicates().hasLanguage(RoslynPlugin.CS_LANGUAGE_KEY)).iterator().hasNext() ||
            fs.files(fs.predicates().hasLanguage(RoslynPlugin.VBNET_LANGUAGE_KEY)).iterator().hasNext();
  }
  
  private String getSolution() {
    File workingDir = reactor.getRoot().getBaseDir();
    File[] solutions = this.finder(workingDir, ".sln");
    if (solutions.length != 1) {
      if (solutions.length > 1) {
        String solution = getEmptyStringOrValue(RoslynPlugin.SOLUTION_KEY);
        if (solution == null || solution.isEmpty()) {
          LOG.error("RoslynSensor : More than one solution found, and none selected: use '{}'", RoslynPlugin.SOLUTION_KEY);
        } else {
          return solution; 
        }        
        for (File solutionprint : solutions) {          
          LOG.debug("RoslynSensor : Solution in root '{}'", solutionprint.getAbsolutePath());
        }        
      } else {
        LOG.debug("RoslynSensor : No solution found in '{}' : will skip RoslynRunner", workingDir);
      }
      return "";
    } else {
      return solutions[0].toPath().toAbsolutePath().toString();
    }    
  }
  
  @Override
  public void analyse(Project project, SensorContext context) {
    analyze(project);    
    try {
      importResults();
    } catch (IOException ex) {
      LOG.error("Failed to parse results file '{}'", ex.getMessage());
    }
  }

  private String getEmptyStringOrValue(String key) {
    String data = settings.getString(key);
    if (data == null) {
      return "";
    }    
    return data;
  }
  
  private Map<String, String> buildAdditionalFileContents(Settings settings) {
    ImmutableMap.Builder<String, String> builder = ImmutableMap.builder();
    String[] additionalFilesIndexes = settings.getStringArray(RoslynPlugin.ADDITIONAL_FILES_KEY);
    for (String additionalFilesIndex : additionalFilesIndexes) {
      String name = getEmptyStringOrValue(RoslynPlugin.ADDITIONAL_FILES_KEY + "." + additionalFilesIndex + "." + RoslynPlugin.ADDITIONAL_FILES_NAME_KEY);
      String content = getEmptyStringOrValue(RoslynPlugin.ADDITIONAL_FILES_KEY + "." + additionalFilesIndex + "." + RoslynPlugin.ADDITIONAL_FILES_CONTENT_KEY);
      
      builder.put(name, content);
    }
    return builder.build();
  }
  
  private void analyze(Project project) {  
    
    String solution = getSolution();
    if ("".equals(solution)) {
      return;
    }
    
    Map<String, String> additionalFiles = buildAdditionalFileContents(this.settings);
    
    String additionalFilesString = "";
    for(Map.Entry<String, String> entry : additionalFiles.entrySet()) {
      try {
        File additionalFile = additionalIncludeFile(entry.getKey(), entry.getValue());
        additionalFilesString += additionalFilesString + additionalFile.getAbsolutePath() + ";";
      } catch (IOException ex) {
        LOG.error("Unable to create additional file for analysis '{}' '{}'", entry.getKey(), ex.getMessage());
      }      
    }
    
    String moduleKey = getModuleKey(project);    
    String projectKey = reactor.getRoot().getKey();
    StringBuilder sb = new StringBuilder();
    appendLine(sb, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    appendLine(sb, "<AnalysisInput>");
    appendLine(sb, "  <Settings>");
    appendLine(sb, "      <SolutionToUse>" + (getSolution()) + "</SolutionToUse>");
    appendLine(sb, "      <ExternalDiagnostics>" + (getEmptyStringOrValue(RoslynPlugin.DIAGNOSTICS_PATH_KEY)) + "</ExternalDiagnostics>");    
    appendLine(sb, "      <SolutionRoot>" + reactor.getRoot().getBaseDir() + "</SolutionRoot>");
    appendLine(sb, "      <SonarUrl>" + (getEmptyStringOrValue("sonar.host.url")) + "</SonarUrl>");
    appendLine(sb, "      <ModuleKey>" + moduleKey + "</ModuleKey>");
    appendLine(sb, "      <ProjectKey>" + projectKey + "</ProjectKey>");
    appendLine(sb, "      <BranchKey>" + (getEmptyStringOrValue("sonar.branch")) + "</BranchKey>");
    appendLine(sb, "      <EnableRules>" + (settings.getBoolean(RoslynPlugin.ENABLE_RULES_KEY) ? "true" : "false") + "</EnableRules>");
    appendLine(sb, "      <UseSonarWebProfile>" + (settings.getBoolean(RoslynPlugin.SYNC_PROFILE_TYPE_KEY) ? "true" : "false") + "</UseSonarWebProfile>");
    appendLine(sb, "      <AdditionalFiles>" + additionalFilesString + "</AdditionalFiles>");
    appendLine(sb, "  </Settings>");
    appendLine(sb, "</AnalysisInput>");

    File analysisInput = toolInput();
    File analysisOutput = toolOutput();

    try {
      Files.write(sb, analysisInput, Charsets.UTF_8);
    } catch (IOException e) {
      throw Throwables.propagate(e);
    }

    File executableFile = extractor.executableFile();    
    String extExec = getEmptyStringOrValue(RoslynPlugin.EXTERNAL_ANALYSER_PATH);
    
    if (!"".equals(extExec)) {
      File extFile = new File(extExec);
      if (extFile.exists()) {
        executableFile = extFile;
      }      
    }

    String username = getEmptyStringOrValue("sonar.login");
    String password = getEmptyStringOrValue("sonar.password");
    
    Command command;
    if (OsUtils.isWindows()) {
      command = Command.create(executableFile.getAbsolutePath())
              .addArgument("/i:" + analysisInput.getAbsolutePath())
              .addArgument("/u:" + username)
              .addArgument("/p:" + password)
              .addArgument("/o:" + analysisOutput.getAbsolutePath());      
    } else {
      command = Command.create("mono")
              .addArgument(executableFile.getAbsolutePath())
              .addArgument("/i:" + analysisInput.getAbsolutePath())
              .addArgument("/u:" + username)
              .addArgument("/p:" + password)              
              .addArgument("/o:" + analysisOutput.getAbsolutePath());  
    }

    command.setEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
    LOG.info(command.toCommandLine().replace(password, "xxxxxx"));
    CommandExecutor.create().execute(command, new LogInfoStreamConsumer(), new LogErrorStreamConsumer(), Integer.MAX_VALUE);
  }

  private void importResults() throws FileNotFoundException, IOException {
    File analysisOutput = toolOutput();

    try(BufferedReader br = new BufferedReader(new FileReader(analysisOutput))) {
        for(String line; (line = br.readLine()) != null; ) {
          String [] elements = line.split(";");
          String path    = elements[0];
          String lineval = elements[1];
          String id      = elements[2];
          String message = elements[3];
                                
          String repository = RoslynPlugin.REPOSITORY_KEY_CS;
          if(path.toLowerCase().endsWith(".vb")) {
            repository = RoslynPlugin.REPOSITORY_KEY_VB;
          }          
          
          InputFile inputFile = fs.inputFile(fs.predicates().is(new File(path)));
          if (inputFile != null) {
            Issuable issuable = perspectives.as(Issuable.class, inputFile);
            if (issuable != null) {
              issuable.addIssue(issuable.newIssueBuilder()
                  .ruleKey(RuleKey.of(repository, id))
                  .message(message)
                  .line(Integer.parseInt(lineval))
                  .build());
            } else {
              LOG.info("issuable not found - issue will not be imported: '{}' : '{}'", path, message);
            }
          } else {
            LOG.info("inputFile not created - issue will not be imported: '{}' : '{}'", path, message);
          }
        }
    }
  }
  
  private void appendLine(StringBuilder sb, String line) {
    sb.append(line);
    sb.append("\r\n");
  }

  private String getModuleKey(Project project) {
    if(project.isModule()) {
      return project.getKey();
    }    
    return "";
  }

  private static class LogInfoStreamConsumer implements StreamConsumer {
    @Override
    public void consumeLine(String line) {
      LOG.info(line);
    }
  }

  private static class LogErrorStreamConsumer implements StreamConsumer {
    @Override
    public void consumeLine(String line) {
      LOG.error(line);
    }
  }

  private File additionalIncludeFile(String fileName, String content) throws IOException {
    File additionalFile = new File(fs.workDir(), fileName);    
    Files.write(content, additionalFile, Charsets.UTF_8);
    return additionalFile;    
  }
  private File toolInput() {
    return new File(fs.workDir(), "roslyn-analysis-input.xml");
  }

  private File toolOutput() {
    return toolOutput(fs);
  }

  public static File toolOutput(FileSystem fileSystem) {
    return new File(fileSystem.workDir(), "roslyn-analysis-output.xml");
  }
}
