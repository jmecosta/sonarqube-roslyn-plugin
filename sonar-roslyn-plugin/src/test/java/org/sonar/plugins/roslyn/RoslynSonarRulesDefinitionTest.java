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

import org.junit.Test;
import org.sonar.api.server.rule.RulesDefinition.Context;

import static org.fest.assertions.Assertions.assertThat;

public class RoslynSonarRulesDefinitionTest {

  @Test
  public void testCSharp() {
    Context context = new Context();
    assertThat(context.repositories()).isEmpty();

    new RulesDefinitionCSharpRoslyn().define(context);

    assertThat(context.repositories()).hasSize(1);
    assertThat(context.repository(RoslynPlugin.REPOSITORY_KEY_CS).rules()).hasSize(1);
  }

  @Test
  public void testVB() {
    Context context = new Context();
    assertThat(context.repositories()).isEmpty();

    new RulesDefinitionVbNetRoslyn().define(context);

    assertThat(context.repositories()).hasSize(1);
    assertThat(context.repository(RoslynPlugin.REPOSITORY_KEY_VB).rules()).hasSize(1);
  }  
}
