#region Copyright (c) 2026 Atif Aziz. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

using Xunit;

namespace IntegrationTests;

/// <summary>
/// Collection fixture that wipes the scenarios directory once before the test run.
/// </summary>
public sealed class ScenarioFixture : IDisposable
{
    public ScenarioFixture() => ScenarioRunner.CleanScenariosDir();

    public void Dispose() { /* nothing to clean up — leave scenarios for inspection */ }
}

[CollectionDefinition(Name)]
public class ScenarioCollection : ICollectionFixture<ScenarioFixture>
{
    public const string Name = "Scenarios";
}
