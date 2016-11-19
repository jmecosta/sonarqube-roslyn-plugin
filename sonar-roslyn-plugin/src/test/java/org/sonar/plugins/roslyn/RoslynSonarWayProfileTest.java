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

import org.sonar.plugins.roslyn.SonarWayProfileCSharp;
import org.junit.Test;
import org.sonar.api.profiles.RulesProfile;
import org.sonar.api.utils.ValidationMessages;

import static org.fest.assertions.Assertions.assertThat;

public class RoslynSonarWayProfileTest {

  @Test
  public void testCSharp() {
    RulesProfile profile = new SonarWayProfileCSharp().createProfile(ValidationMessages.create());
    assertThat(profile.getActiveRules()).hasSize(0);
  }

  @Test
  public void testVBNet() {
    RulesProfile profile = new SonarWayProfileVbNet().createProfile(ValidationMessages.create());
    assertThat(profile.getActiveRules()).hasSize(0);
  }  
}
