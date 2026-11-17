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

`.gitlab-ci.yml` packs the library on a Linux runner, runs the tests on a Windows runner and, for tags like `v1.59.0`, publishes the package to this project's GitLab package registry. Set the `WINDOWS_RUNNER_TAG` variable if your Windows runner uses a different tag.

To consume the package, add the registry as a NuGet source:

```
dotnet nuget add source "https://<gitlab-host>/api/v4/projects/<project-id>/packages/nuget/index.json" --name gitlab --username <user> --password <token>
dotnet add package __COMPANY__.FakeXrmEasy.v9
```

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
