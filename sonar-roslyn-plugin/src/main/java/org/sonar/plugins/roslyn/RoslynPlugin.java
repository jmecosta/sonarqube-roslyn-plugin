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


import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import org.sonar.api.Plugin;
import org.sonar.api.PropertyType;
import org.sonar.api.config.PropertyDefinition;
import org.sonar.api.config.PropertyFieldDefinition;
import org.sonar.api.resources.Qualifiers;

public class RoslynPlugin implements Plugin {          
  private static List<PropertyDefinition> generalProperties() {

    return new ArrayList<>(Arrays.asList(
      PropertyDefinition.builder(RoslynPlugin.SOLUTION_KEY)
      .name("Solution to analyse")
      .description("Solution to analyse.")
      .onlyOnQualifiers(Qualifiers.PROJECT)
      .index(1)
      .build(),
      PropertyDefinition.builder(RoslynPlugin.EXTERNAL_ANALYSER_PATH)
      .name("RoslynRunner path")
      .description("RoslynRunner path, leave empty to use the embeded one.")
      .onQualifiers(Qualifiers.PROJECT)
      .build(),
      PropertyDefinition.builder(RoslynPlugin.DIAGNOSTICS_PATH_KEY)
      .name("External paths containing diagnostics")
      .description("Only absolute paths are supported, and should be kepted outside Solution folder. Default path C:\\ProgramData\\VSSonarExtension\\Diagnostics\\UserDiagnostics is always loaded.")
      .onQualifiers(Qualifiers.PROJECT)
      .build(),      
      PropertyDefinition.builder(RoslynPlugin.ENABLE_RULES_KEY)
      .name("Enable rules")
      .type(PropertyType.BOOLEAN)
      .defaultValue("true")
      .description("All created rules will be enabled in the current profile")
      .build(),
      PropertyDefinition.builder(RoslynPlugin.SYNC_PROFILE_TYPE_KEY)
      .name("Sync type")
      .type(PropertyType.BOOLEAN)
      .defaultValue("true")
      .onQualifiers(Qualifiers.PROJECT)
      .description("If true sonar will enforce profile define in sonar web, if false profile found in ruleset will be sync in sonar server. A new profile will be created per project. This will be ignored if sync type is false.  (might require 2 runs to have everything synched)")
      .build(),
      PropertyDefinition.builder(RoslynPlugin.ADDITIONAL_FILES_KEY)
        .name("Additional files key.")
        .onQualifiers(Qualifiers.PROJECT, Qualifiers.MODULE)
        .description("Content of file with options to be passed to diagnostics See <a href='https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/Configuration.md'>this page</a> for example.")
        .fields(
          PropertyFieldDefinition.build(RoslynPlugin.ADDITIONAL_FILES_NAME_KEY)
            .name("File name")
            .description("File name to be used")
            .type(PropertyType.STRING)
            .build(),
          PropertyFieldDefinition.build(RoslynPlugin.ADDITIONAL_FILES_CONTENT_KEY)
            .name("Content")
            .description("Content of the file to be used")
            .type(PropertyType.TEXT)
            .build()
        ).build()));
  }

  public static final String CS_LANGUAGE_KEY = "cs";
  public static final String CS_LANGUAGE_NAME = "C#";
  public static final String VBNET_LANGUAGE_KEY = "vbnet";
  public static final String VBNET_LANGUAGE_NAME = "vbnet";
 
  public static final String ROSLYN_PROFILE_NAME = "Sonar way";

  public static final String REPOSITORY_KEY_CS = "roslyn-cs";
  public static final String REPOSITORY_KEY_VB = "roslyn-vbnet";
  public static final String REPOSITORY_NAME = "Roslyn";

  public static final String EXTERNAL_ANALYSER_PATH = "sonar.roslyn.runner.path";  
  public static final String SOLUTION_KEY = "sonar.roslyn.solution";  
  public static final String DIAGNOSTICS_PATH_KEY = "sonar.roslyn.diagnostic.path";  
  public static final String ENABLE_RULES_KEY = "sonar.roslyn.enable.rules";
  public static final String SYNC_PROFILE_TYPE_KEY = "sonar.roslyn.sync.type";
  public static final String ADDITIONAL_FILES_KEY = "sonar.roslyn.additional.files";
  public static final String ADDITIONAL_FILES_NAME_KEY = "sonar.roslyn.additional.name";
  public static final String ADDITIONAL_FILES_CONTENT_KEY = "sonar.roslyn.additional.content";
   
  
  @Override
  public void define(Context cntxt) {
    List<Object> l = new ArrayList<>();
    l.add(RulesDefinitionCSharpRoslyn.class);
    l.add(RulesDefinitionVbNetRoslyn.class);
    l.add(SonarWayProfileCSharp.class);
    l.add(SonarWayProfileVbNet.class);
    l.add(RoslynRunnerExtractor.class);
    l.add(RoslynSensor.class);
    
    l.addAll(generalProperties());
    cntxt.addExtensions(l);
  }

}
