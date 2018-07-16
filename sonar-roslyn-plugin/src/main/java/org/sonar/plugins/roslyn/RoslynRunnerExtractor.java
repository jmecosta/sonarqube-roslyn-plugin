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

import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import org.sonar.api.batch.InstantiationStrategy;
import org.sonar.api.batch.ScannerSide;
import org.sonar.api.batch.bootstrap.ProjectReactor;
import org.sonar.api.utils.log.Logger;
import org.sonar.api.utils.log.Loggers;

@InstantiationStrategy(InstantiationStrategy.PER_BATCH)
@ScannerSide()
public class RoslynRunnerExtractor {

  public static final Logger LOG = Loggers.get(RoslynRunnerExtractor.class);
  private static final String N_SONARQUBE_ANALYZER = "RoslynRunner";
  private static final String N_SONARQUBE_ANALYZER_ZIP = N_SONARQUBE_ANALYZER + ".zip";
  private static final String N_SONARQUBE_ANALYZER_EXE = N_SONARQUBE_ANALYZER + ".exe";

  private final ProjectReactor reactor;
  private File file = null;

  public RoslynRunnerExtractor(ProjectReactor reactor) {
    this.reactor = reactor;
  }

  public File executableFile() throws IOException {
    if (file == null) {
      file = unzipProjectCheckerFile(N_SONARQUBE_ANALYZER_EXE);
    }

    return file;
  }

  private File unzipProjectCheckerFile(String fileName) throws IOException {
    File workingDir = reactor.getRoot().getWorkDir();
    File toolWorkingDir = new File(workingDir, N_SONARQUBE_ANALYZER);
    File zipFile = new File(workingDir, N_SONARQUBE_ANALYZER_ZIP);
    
    if (zipFile.exists()) {
      return new File(toolWorkingDir, fileName);
    }

    try {

      
      try (InputStream is = getClass().getResourceAsStream("/" + N_SONARQUBE_ANALYZER_ZIP)) {
        Files.copy(is, zipFile.toPath());
      }

      UnZip unZip = new UnZip();
      unZip.unZipIt(zipFile.getAbsolutePath(),toolWorkingDir.getAbsolutePath());
        
      return new File(toolWorkingDir, fileName);
    } catch (IOException e) {
      LOG.error("Unable tp unzip File: {} => {}", fileName, e.getMessage());
      throw e;
    }
  }
}
