/*
 * Sonar Roslyn Plugin :: Core
 * Copyright (C) 2016-2018 jmecsoftware.com
 * mailto:info AT sonarsource DOT com
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
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */
/*
 * Sonar Roslyn Plugin, open source software quality management tool.
 * Author(s) : Jorge Costa @ jmecsoftware.com
 *
 * Sonar Roslyn Plugin is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * Sonar Roslyn Plugin is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 */
package org.sonar.plugins.roslyn;

import java.io.BufferedReader;

import org.sonar.api.batch.DependedUpon;
import org.sonar.api.batch.fs.FileSystem;
import org.sonar.api.batch.fs.InputFile;
import org.sonar.api.rule.RuleKey;
import org.sonar.api.utils.command.Command;
import org.sonar.api.utils.command.CommandExecutor;
import org.sonar.api.utils.command.StreamConsumer;

import java.io.File;
import java.io.FileNotFoundException;
import java.io.FileReader;
import java.io.FilenameFilter;
import java.io.IOException;
import java.nio.file.Files;
import java.util.HashMap;
import java.util.Map;
import org.sonar.api.batch.sensor.Sensor;
import org.sonar.api.batch.sensor.SensorContext;
import org.sonar.api.batch.sensor.SensorDescriptor;
import org.sonar.api.batch.sensor.issue.NewIssue;
import org.sonar.api.batch.sensor.issue.NewIssueLocation;
import org.sonar.api.config.Configuration;
import org.sonar.api.utils.log.Logger;
import org.sonar.api.utils.log.Loggers;

@DependedUpon("RoslynRunnerExtractor")
public class RoslynSensor implements Sensor {
  
  public static String ROSLYN_SENSOR_ENABLED = "sonar.roslyn.enabled";
  
  public static final Logger LOG = Loggers.get(RoslynSensor.class);
  private final RoslynRunnerExtractor extractor;
  private final Configuration settings;

  public RoslynSensor(RoslynRunnerExtractor extractor, Configuration settings) {
    this.extractor = extractor;
    this.settings = settings;
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
  public void describe(SensorDescriptor descriptor) {
    descriptor.global().onlyOnLanguage(RoslynPlugin.CS_LANGUAGE_KEY).name("RoslynSensor");
  }

  @Override
  public void execute(SensorContext context) {
    if (!settings.getBoolean(ROSLYN_SENSOR_ENABLED).isPresent()) {
      LOG.info("Roslyn Sensor not present - Disabled");
      return;
    }
    
    if (!settings.getBoolean(ROSLYN_SENSOR_ENABLED).get()) {
      LOG.info("Roslyn Sensor - Disabled");
      return;
    } 
    
    LOG.info("Execute Roslyn Sensor : " + context.fileSystem().baseDir());
    try {
      String solution = getSolution(context.fileSystem().baseDir(), context);
      if ("".equals(solution)) {
        LOG.info("Roslyn Sensor will skip. No solution found at this level");
        return;
      } 
      analyze(context, solution);
      importResults(context);
    } catch (IOException ex) {
      LOG.error("Failed to parse results file '{}'", ex.getMessage());
      context.newAnalysisError().message("Failed to parse results file " +  ex.getMessage()).save();
    }
  }
    
  private String getSolution(File startFolder, SensorContext context) {
    
    if (startFolder == null) {
      LOG.debug("RoslynSensor : No solution found");
      return "";
    }
    File[] solutions = this.finder(startFolder, ".sln");
    if (solutions.length != 1) {
      if (solutions.length > 1) {
        String solution = getEmptyStringOrValue(context, RoslynPlugin.SOLUTION_KEY);
        if (solution == null || solution.isEmpty()) {
          LOG.error("RoslynSensor : More than one solution found, and none selected: use '{}'", RoslynPlugin.SOLUTION_KEY);
        } else {
          return solution; 
        }
        
        for (File solutionprint : solutions) {          
          LOG.debug("RoslynSensor : Solution in root '{}'", solutionprint.getAbsolutePath());
        }        
      } else {
        return getSolution(startFolder.getParentFile(), context);
      }
      return "";
    } else {
      return solutions[0].toPath().toAbsolutePath().toString();
    }    
  }
  
  private String getEmptyStringOrValue(SensorContext ctx, String key) {
    if (ctx.config().get(key).isPresent()) {
      return ctx.config().get(key).get();
    }
    
    return "";    
  }
  
  private Map<String, String> buildAdditionalFileContents(SensorContext cxt) {
    Map<String, String> builder = new HashMap<String, String>();
    String[] additionalFilesIndexes = cxt.config().getStringArray(RoslynPlugin.ADDITIONAL_FILES_KEY);
    for (String additionalFilesIndex : additionalFilesIndexes) {
      String name = getEmptyStringOrValue(cxt, RoslynPlugin.ADDITIONAL_FILES_KEY + "." + additionalFilesIndex + "." + RoslynPlugin.ADDITIONAL_FILES_NAME_KEY);
      String content = getEmptyStringOrValue(cxt, RoslynPlugin.ADDITIONAL_FILES_KEY + "." + additionalFilesIndex + "." + RoslynPlugin.ADDITIONAL_FILES_CONTENT_KEY);
      
      builder.put(name, content);
    }
    return builder;
  }
  
  private void analyze(SensorContext ctx, String solution) throws IOException {  
        
    Map<String, String> additionalFiles = buildAdditionalFileContents(ctx);
    
    String additionalFilesString = "";
    for(Map.Entry<String, String> entry : additionalFiles.entrySet()) {
      try {
        File additionalFile = additionalIncludeFile(entry.getKey(), entry.getValue(), ctx);
        additionalFilesString += additionalFilesString + additionalFile.getAbsolutePath() + ";";
      } catch (IOException ex) {
        LOG.error("Unable to create additional file for analysis '{}' '{}'", entry.getKey(), ex.getMessage());
      }      
    }
    
    String projectKey = ctx.config().get("sonar.projectKey").get();
    StringBuilder sb = new StringBuilder();
    appendLine(sb, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    appendLine(sb, "<AnalysisInput>");
    appendLine(sb, "  <Settings>");
    appendLine(sb, "      <SolutionToUse>" + solution + "</SolutionToUse>");
    appendLine(sb, "      <ExternalDiagnostics>" + (getEmptyStringOrValue(ctx, RoslynPlugin.DIAGNOSTICS_PATH_KEY)) + "</ExternalDiagnostics>");    
    appendLine(sb, "      <SolutionRoot>" + ctx.fileSystem().baseDir() + "</SolutionRoot>");
    appendLine(sb, "      <SonarUrl>" + (getEmptyStringOrValue(ctx, "sonar.host.url")) + "</SonarUrl>");
    appendLine(sb, "      <ProjectKey>" + projectKey + "</ProjectKey>");
    appendLine(sb, "      <BranchKey>" + (getEmptyStringOrValue(ctx, "sonar.branch")) + "</BranchKey>");
    appendLine(sb, "      <EnableRules>" + (ctx.config().getBoolean(RoslynPlugin.ENABLE_RULES_KEY).get() ? "true" : "false") + "</EnableRules>");
    appendLine(sb, "      <UseSonarWebProfile>" + (ctx.config().getBoolean(RoslynPlugin.SYNC_PROFILE_TYPE_KEY).get() ? "true" : "false") + "</UseSonarWebProfile>");
    appendLine(sb, "      <AdditionalFiles>" + additionalFilesString + "</AdditionalFiles>");
    appendLine(sb, "  </Settings>");
    appendLine(sb, "</AnalysisInput>");

    File analysisInput = toolInput(ctx);
    File analysisOutput = toolOutput(ctx);

    Files.write(analysisInput.toPath(), sb.toString().getBytes());
    File executableFile = extractor.executableFile();    
    String extExec = getEmptyStringOrValue(ctx, RoslynPlugin.EXTERNAL_ANALYSER_PATH);
    
    if (!"".equals(extExec)) {
      File extFile = new File(extExec);
      if (extFile.exists()) {
        executableFile = extFile;
      }      
    }

    String username = getEmptyStringOrValue(ctx, "sonar.login");
    String password = getEmptyStringOrValue(ctx, "sonar.password");
    
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

  private void importResults(SensorContext ctx) throws FileNotFoundException, IOException {
    File analysisOutput = toolOutput(ctx);

    LOG.info("Import data from: " + analysisOutput);
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
          
          InputFile inputFile = ctx.fileSystem().inputFile(ctx.fileSystem().predicates().is(new File(path)));
          if (inputFile != null) {
            NewIssue newIssue = ctx.newIssue().forRule(RuleKey.of(repository, id));
            NewIssueLocation location = newIssue.newLocation()
              .on(inputFile)
              .at(inputFile.selectLine(Integer.parseInt(lineval)))
              .message(message);

            newIssue.at(location);
            newIssue.save();
         
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

    private String getModuleKey(SensorContext context) {
    if(context.module().key() != "") {
      return context.module().key();
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

  private File additionalIncludeFile(String fileName, String content, SensorContext ctx) throws IOException {
    File additionalFile = new File(ctx.fileSystem().workDir(), fileName);    
    Files.write(additionalFile.toPath(), content.getBytes());
    return additionalFile;    
  }
  private File toolInput(SensorContext ctx) {
    return new File(ctx.fileSystem().workDir(), "roslyn-analysis-input.xml");
  }

  private File toolOutput(SensorContext ctx) {
    return toolOutput(ctx.fileSystem());
  }

  public static File toolOutput(FileSystem fileSystem) {
    return new File(fileSystem.workDir(), "roslyn-analysis-output.xml");
  }
}
