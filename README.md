# FakeXrmEasy v1 (on-premises v9 fork)

In-memory fake of `IOrganizationService` for unit testing plugins, custom workflow activities and other code against Dynamics 365 Customer Engagement on-premises v9.

This is a maintained fork of [FakeXrmEasy v1](https://github.com/jordimontana82/fake-xrm-easy) (MIT), which is no longer maintained upstream. Support for CRM 2011 to 8.2 has been removed.

## Requirements

- .NET SDK 8 or later (for `dotnet build` / `dotnet test`)
- .NET Framework 4.6.2 or later to run the tests (Windows)

The library targets `net462`, the framework version Microsoft requires for v9 plugin assemblies. Test projects that consume it may target any .NET Framework version from 4.6.2 up to 4.8.1.

## Build and test

```
dotnet build
dotnet test
```

## Layout

| Path | Content |
|------|---------|
| `src/FakeXrmEasy` | The library |
| `tests/FakeXrmEasy.Tests` | xUnit tests |
| `docs` | [Usage guide](docs/USAGE.md) |

## CI and releases

Two equivalent pipelines exist. Both pack on Linux, run the tests on Windows and publish the package for tags like `v1.59.0`:

| Pipeline | File | Publishes to |
|----------|------|--------------|
| GitHub Actions | `.github/workflows/ci.yml` | GitHub Packages (`https://nuget.pkg.github.com/lbaumdeveloper/index.json`) |
| GitLab CI | `.gitlab-ci.yml` | The GitLab project's package registry. Set `WINDOWS_RUNNER_TAG` if your Windows runner uses a different tag. |

### Install from GitHub Packages

GitHub's NuGet registry requires authentication even for packages of public repositories. Create a personal access token (classic) with the `read:packages` scope, then:

```
dotnet nuget add source "https://nuget.pkg.github.com/lbaumdeveloper/index.json" --name github --username <github-user> --password <token> --store-password-in-clear-text
dotnet add package Reforged.FakeXrmEasy.v9
```

In CI, a GitHub Actions workflow in another repository can use a token with `read:packages`; within this repository `GITHUB_TOKEN` is enough.

### Install from GitLab

```
dotnet nuget add source "https://<gitlab-host>/api/v4/projects/<project-id>/packages/nuget/index.json" --name gitlab --username <user> --password <token>
dotnet add package Reforged.FakeXrmEasy.v9
```

### Create a test project

The package does not bring a test framework. Create a .NET Framework 4.6.2 class library and add the test framework yourself, for example xUnit:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Reforged.FakeXrmEasy.v9" Version="1.59.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Add a `ProjectReference` to your plugin project and `<Reference Include="System.Activities" />` when you test custom workflow activities. See [docs/USAGE.md](docs/USAGE.md) for the full setup.

## Usage

A test creates an `XrmFakedContext`, seeds it with records, runs the code under test and asserts on the in-memory data:

```csharp
[Fact]
public void Post_create_creates_follow_up_task()
{
    var context = new XrmFakedContext();
    var account = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso Ltd" };

    context.ExecutePluginWithTarget<AccountFollowUpPlugin>(account, "Create", (int)ProcessingStepStage.Postoperation);

    var task = context.CreateQuery("task").Single();
    Assert.Equal("Welcome call for Contoso Ltd", task["subject"]);
}
```

Code that only needs an `IOrganizationService` gets it from `context.GetOrganizationService()`.

The [usage guide](docs/USAGE.md) has tested examples for:

- [Setting up a test project and the faked context](docs/USAGE.md#1-set-up-a-test-project): seeding data, `CallerId`, early-bound entities
- [Plugins](docs/USAGE.md#3-testing-plugins): target, full execution context, pre/post images, exceptions, tracing, configuration
- [Pipeline simulation](docs/USAGE.md#4-pipeline-simulation): `RegisterPluginStep` with images and filtering attributes
- [Custom workflow activities](docs/USAGE.md#5-testing-custom-workflow-activities): inputs, outputs, workflow context
- [Queries](docs/USAGE.md#6-queries): QueryExpression, FetchXML with aggregates, LINQ, user and team operators
- [Built-in messages](docs/USAGE.md#7-built-in-messages): SetState, Assign, SendEmail, Associate, ExecuteMultiple, WhoAmI
- [Rollup fields](docs/USAGE.md#8-rollup-fields): `RegisterRollupField` with `RollupFields.Count/Sum/Avg/Min/Max`
- [Custom fakes](docs/USAGE.md#9-custom-fakes): custom actions, execution mocks, replacing built-in executors
- [Troubleshooting](docs/USAGE.md#10-troubleshooting)

## License

MIT, see [LICENSE.md](LICENSE.md).
