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

## CI and releases

`.gitlab-ci.yml` packs the library on a Linux runner, runs the tests on a Windows runner and, for tags like `v1.59.0`, publishes the package to this project's GitLab package registry. Set the `WINDOWS_RUNNER_TAG` variable if your Windows runner uses a different tag.

To consume the package, add the registry as a NuGet source:

```
dotnet nuget add source "https://<gitlab-host>/api/v4/projects/<project-id>/packages/nuget/index.json" --name gitlab --username <user> --password <token>
dotnet add package __COMPANY__.FakeXrmEasy.v9
```

## Usage

```csharp
var context = new XrmFakedContext();
var account = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso" };
context.Initialize(new[] { account });

context.ExecutePluginWithTarget<MyPlugin>(account);
var service = context.GetOrganizationService();
```

## License

MIT, see [LICENSE.md](LICENSE.md).
