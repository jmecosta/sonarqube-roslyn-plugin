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

import com.google.common.base.Throwables;
import com.google.common.io.ByteStreams;
import com.google.common.io.Files;
import org.sonar.api.BatchExtension;
import org.sonar.api.batch.InstantiationStrategy;
import org.sonar.api.batch.bootstrap.ProjectReactor;

import java.io.File;
import java.io.IOException;
import java.io.InputStream;

@InstantiationStrategy(InstantiationStrategy.PER_BATCH)
public class RoslynRunnerExtractor implements BatchExtension {

  private static final String N_SONARQUBE_ANALYZER = "RoslynRunner";
  private static final String N_SONARQUBE_ANALYZER_ZIP = N_SONARQUBE_ANALYZER + ".zip";
  private static final String N_SONARQUBE_ANALYZER_EXE = N_SONARQUBE_ANALYZER + ".exe";

  private final ProjectReactor reactor;
  private File file = null;

  public RoslynRunnerExtractor(ProjectReactor reactor) {
    this.reactor = reactor;
  }

  public File executableFile() {
    if (file == null) {
      file = unzipNSonarQubeAnalyzer();
    }

    return file;
  }

  private File unzipNSonarQubeAnalyzer() {
    File workingDir = reactor.getRoot().getWorkDir();
    File toolWorkingDir = new File(workingDir, N_SONARQUBE_ANALYZER);
    File zipFile = new File(workingDir, N_SONARQUBE_ANALYZER_ZIP);

    try {
      Files.createParentDirs(zipFile);

      InputStream is = getClass().getResourceAsStream("/" + N_SONARQUBE_ANALYZER_ZIP);
      try {
        Files.write(ByteStreams.toByteArray(is), zipFile);
      } finally {
        is.close();
      }

      new Zip(zipFile).unzip(toolWorkingDir);

      return new File(toolWorkingDir, N_SONARQUBE_ANALYZER_EXE);
    } catch (IOException e) {
      throw Throwables.propagate(e);
    }
  }

}
