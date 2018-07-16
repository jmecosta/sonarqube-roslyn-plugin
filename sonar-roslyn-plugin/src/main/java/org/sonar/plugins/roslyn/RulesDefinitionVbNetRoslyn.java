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

import org.sonar.api.rule.Severity;
import org.sonar.api.server.rule.RulesDefinition;

public class RulesDefinitionVbNetRoslyn implements RulesDefinition {

  @Override
  public void define(Context context) {
    NewRepository repository = context
      .createRepository(RoslynPlugin.REPOSITORY_KEY_VB, RoslynPlugin.VBNET_LANGUAGE_KEY)
      .setName(RoslynPlugin.REPOSITORY_NAME);

    repository
      .createRule("TemplateRule")
      .setName("Template Rule")
      .setSeverity(Severity.MAJOR)
      .setTemplate(true)
      .setHtmlDescription("<p>template rule<p>");
    repository.done();
  }
}
