
sonar-roslyn-plugin
=========

sonar-roslyn-plugin - Plugin for SonarQube to import roslyn issues

### License
This program is free software; you can redistribute it and/or modify it under the terms of the GNU Lesser General Public License as published by the Free Software Foundation; either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public License for more details. You should have received a copy of the GNU Lesser General Public License along with this program; if not, write to the Free Software Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA


## How to compile
Compile with netbeans. Be sure you have visual studio 2015 installed

## Make changes
Use visual studio or netbeans, and create pull requests.

## Installation
Copy jar to extension/plugins in you sonarqube server instance. Restart,

## Alternatives
https://github.com/SonarSource-VisualStudio/sonarlint-visualstudio
https://github.com/SonarSource-VisualStudio/sonarqube-roslyn-sdk/ you can code your own solution for your analysers.

## why
So far Sonarlint is quite verbose to the solution you are installing, managing multiple solutions is a very tedious work and updating each version will cause huge ammount of changes into project files. Now try to handle a project that contains dozens of solutions and you can find yourself spending huge ammounts of time maintaining those.

The idea of this, in conjuntion with VSSonarQubeExtension (https://github.com/TrimbleSolutionsCorporation/VSSonarQubeExtension), is that you dont need to touch your project files to accomplish similar experience as SonarLint. You just install the VSSonarQubeExtension, install the plugin in SonarQube, setup the location of your diagnostics (you can distribute those any way you like, we are using choco) and you are all set. All solutions that you work are sharing the same environment.

Updates are also easy to handle, you just update the diagnostics and you are all set.

Synchronization of settings is handle in SonarQube ui, so your users dont need to worry about anything.

## Usage
There are multiple ways of using it, but the simplest is to drop your analysers in some external location to the solution. Choco is a nice way of distributing those. And setup that path via SonarQube ui. And you are done.
