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

## Usage

```csharp
var context = new XrmFakedContext();
context.Initialize(new[] { new Account { Id = Guid.NewGuid(), Name = "Contoso" } });

var service = context.GetOrganizationService();
context.ExecutePluginWithTarget<MyPlugin>(target);
```

## License

MIT, see [LICENSE.md](LICENSE.md).
