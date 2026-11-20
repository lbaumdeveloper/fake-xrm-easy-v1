# Test base class

`XrmFakedTestBase` is an abstract class your test classes can inherit from. It owns an `XrmFakedContext`, collects seed records in a list, loads them into the context when you need them, and adds shortcuts for CRUD, queries, plugins and custom workflow activities.

The class has no dependency on a test framework. The examples use NUnit 4.

Every snippet on this page is taken from [`tests/FakeXrmEasy.Tests/FakeContextTests/XrmFakedTestBaseTests.cs`](../tests/FakeXrmEasy.Tests/FakeContextTests/XrmFakedTestBaseTests.cs), which runs with the test suite. The plugins and the workflow activity used in the examples are defined in the [usage guide examples](../tests/FakeXrmEasy.Tests/UsageGuide/UsageGuideExamples.cs). If you change an example here, change it there too.

- [1. Inherit the class](#1-inherit-the-class)
- [2. Seed records](#2-seed-records)
- [3. Configure the context](#3-configure-the-context)
- [4. CRUD and queries](#4-crud-and-queries)
- [5. Plugins](#5-plugins)
- [6. Custom workflow activities](#6-custom-workflow-activities)
- [7. Member reference](#7-member-reference)

## 1. Inherit the class

```csharp
public class AccountPluginTests : XrmFakedTestBase
{
    [SetUp]
    public void SetUp() => ResetContext();

    [Test]
    public void ExecutePluginOnCreate_runs_the_plugin_against_the_seeded_context()
    {
        var account = new Entity("account") { ["name"] = "Contoso Ltd" };
        Records.Add(account);

        ExecutePluginOnCreate<AccountFollowUpPlugin>(account);

        var task = Single("task", t => t.GetAttributeValue<EntityReference>("regardingobjectid")?.Id == account.Id);
        Assert.That(task.GetAttributeValue<string>("subject"), Is.EqualTo("Welcome call for Contoso Ltd"));
    }
}
```

NUnit uses one instance of a test class for all its tests. Call `ResetContext()` in `[SetUp]`, or records and the context leak from one test into the next. xUnit creates a new instance per test and does not need it.

## 2. Seed records

Add records to `Records`. They are loaded into the context the first time you use `Service` or any helper, or when you call `InitializeContext()` yourself.

```csharp
var account = new Entity("account") { ["name"] = "Contoso Ltd" };
Records.Add(account);

var accounts = RetrieveMultiple(new QueryExpression("account") { ColumnSet = new ColumnSet("name") });

Assert.That(accounts.Single().GetAttributeValue<string>("name"), Is.EqualTo("Contoso Ltd"));
Assert.That(accounts.Single().Id, Is.EqualTo(account.Id));
Assert.That(account.Id, Is.Not.EqualTo(Guid.Empty));
```

Rules:

- A record without an `Id` gets a new one when it is loaded. If you need a reference before that, for example a lookup in another seed record, set `Id = Guid.NewGuid()` yourself. `ToEntityReference()` on a record without an `Id` gives an empty id.
- Records added after the first load are loaded on the next call. `InitializeContext()` can be called any number of times.
- `Records` is append only. Removing a record after it was loaded does not remove it from the context.
- The context stores copies. Changing a seed instance after it was loaded does not change the context. Use `Reload` to read the current state.

```csharp
Records.Add(new Entity("account") { ["name"] = "First" });
InitializeContext();

Records.Add(new Entity("account") { ["name"] = "Second" });
InitializeContext(new Entity("account") { ["name"] = "Third" });

Assert.That(Count("account"), Is.EqualTo(3));
Assert.That(GetAll("account").Select(a => a.GetAttributeValue<string>("name")), Is.EquivalentTo(new[] { "First", "Second", "Third" }));
```

```csharp
var account = new Entity("account") { ["name"] = "Original" };
Records.Add(account);
InitializeContext();

account["name"] = "Changed";

Assert.That(Reload(account).GetAttributeValue<string>("name"), Is.EqualTo("Original"));
```

### Run as a specific user

`AddUser` adds a `systemuser` record with an id. `SetCaller` makes it the user for all following requests, plugins and workflow activities.

```csharp
var user = AddUser("Lukas Baum");
SetCaller(user);
```

## 3. Configure the context

Override `CreateContext` to configure the context once for all tests of a class, for example with proxy types, metadata, `UsePipelineSimulation` or custom actions.

```csharp
public class XrmFakedTestBaseCustomContextTests : XrmFakedTestBase
{
    [SetUp]
    public void SetUp() => ResetContext();

    protected override XrmFakedContext CreateContext()
    {
        var context = base.CreateContext();
        context.MaxRetrieveCount = 1;
        return context;
    }

    [Test]
    public void CreateContext_override_configures_the_context()
    {
        Assert.That(Context.MaxRetrieveCount, Is.EqualTo(1));
    }
}
```

`Context` gives you the full `XrmFakedContext` API. Call `InitializeContext()` before using it directly so pending records are loaded.

## 4. CRUD and queries

Methods that take columns return all columns when you pass none.

```csharp
var account = new Account { Id = Guid.NewGuid(), Name = "Contoso Ltd" };
Records.Add(account);

Assert.That(Retrieve<Account>(account.Id).Name, Is.EqualTo("Contoso Ltd"));
Assert.That(GetAll<Account>(), Has.Count.EqualTo(1));
Assert.That(Query<Account>().Single(a => a.Name == "Contoso Ltd").Id, Is.EqualTo(account.Id));
```

```csharp
Records.Add(new Entity("account") { ["name"] = "Contoso" });
Records.Add(new Entity("account") { ["name"] = "Fabrikam" });

var byQuery = RetrieveMultiple(new QueryExpression("account")
{
    ColumnSet = new ColumnSet("name"),
    Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, "Contoso") } }
});
var byFetch = RetrieveMultiple("<fetch><entity name='account'><attribute name='name' /><filter><condition attribute='name' operator='eq' value='Fabrikam' /></filter></entity></fetch>");

Assert.That(byQuery.Single().GetAttributeValue<string>("name"), Is.EqualTo("Contoso"));
Assert.That(byFetch.Single().GetAttributeValue<string>("name"), Is.EqualTo("Fabrikam"));
```

```csharp
var account = new Entity("account") { Id = Guid.NewGuid() };
Records.Add(account);
Assert.That(Exists(account.ToEntityReference()), Is.True);

Delete(account.ToEntityReference());

Assert.That(Exists(account.ToEntityReference()), Is.False);
```

## 5. Plugins

`ExecutePluginOnCreate`, `ExecutePluginOnUpdate` and `ExecutePluginOnDelete` build the execution context, run the plugin and return the context so you can assert output parameters or shared variables. Images are registered as `PreImage` and `PostImage` (`PreImageName`, `PostImageName`). The stage defaults to `PostOperationStage`; `PreValidationStage` and `PreOperationStage` are available as constants.

The plugin only runs. The target is not saved, so assert on the target instance for pre-operation plugins.

### Update with a pre-image and trace output

```csharp
var contact = new Entity("contact") { Id = Guid.NewGuid(), ["lastname"] = "Baum" };
var preImage = new Entity("contact") { Id = contact.Id, ["firstname"] = "Lukas", ["lastname"] = "Old" };

var pluginContext = ExecutePluginOnUpdate<ContactNormalizePlugin>(contact, preImage, stage: PreOperationStage);

Assert.That(contact.GetAttributeValue<string>("fullname"), Is.EqualTo("Lukas Baum"));
Assert.That(pluginContext.PrimaryEntityId, Is.EqualTo(contact.Id));
Assert.That(TraceLog, Does.Contain("Full name set to Lukas Baum"));
```

### Delete and expected exceptions

```csharp
var account = new Entity("account") { Id = Guid.NewGuid() };
Records.Add(account);
Records.Add(new Entity("opportunity") { ["parentaccountid"] = account.ToEntityReference(), ["statecode"] = new OptionSetValue(0) });

Action delete = () => ExecutePluginOnDelete<PreventAccountDeletePlugin>(account.ToEntityReference(), stage: PreValidationStage);

Assert.That(delete, Throws.TypeOf<InvalidPluginExecutionException>());
```

### Custom execution context and configured plugins

Use `CreatePluginContext` for any message, and the `ExecutePlugin` overload taking an instance for plugins with a constructor, such as one reading its unsecure or secure configuration. Change the returned context before executing when you need more, for example `Depth`, `ParentContext` or `SharedVariables`.

```csharp
var account = new Entity("account") { Id = Guid.NewGuid() };

ExecutePlugin(new DefaultCreditLimitPlugin("1500", null), CreatePluginContext("Create", account, PreOperationStage));

Assert.That(account.GetAttributeValue<Money>("creditlimit").Value, Is.EqualTo(1500m));
```

## 6. Custom workflow activities

`ExecuteWorkflowActivity` takes the primary record and the input arguments by property name, and returns the output arguments by property name.

```csharp
var user = AddUser("Lukas Baum");
var account = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso Ltd" };
Records.Add(account);
SetCaller(user);

var outputs = ExecuteWorkflowActivity<CreatePhoneCallActivity>(account.ToEntityReference(), new Dictionary<string, object>
{
    ["Subject"] = "Call back",
    ["DueInDays"] = 2
});

var phoneCall = Retrieve((EntityReference)outputs["PhoneCall"]);
Assert.That(phoneCall.GetAttributeValue<string>("subject"), Is.EqualTo("Call back"));
Assert.That(phoneCall.GetAttributeValue<EntityReference>("regardingobjectid").Id, Is.EqualTo(account.Id));
Assert.That(phoneCall.GetAttributeValue<EntityReference>("createdby").Id, Is.EqualTo(user.Id));
```

For a custom workflow context, build it with `CreateWorkflowContext(primaryEntity)`, change it, and pass it to the `ExecuteWorkflowActivity<TActivity>(workflowContext, inputs)` overload.

## 7. Member reference

The constants are `public`, all other members are `protected`.

| Member | Purpose |
| --- | --- |
| `Context` | The `XrmFakedContext`, created on first use through `CreateContext()`. |
| `Records` | Seed records, loaded into the context on first use. |
| `Service` | `IOrganizationService` of the context. Loads pending records first. |
| `TracingService`, `TraceLog` | Tracing service passed to plugins and activities, and everything traced so far. |
| `CreateContext()` | Virtual. Override to configure the context. |
| `ResetContext()` | New context and empty `Records`. Call it in `[SetUp]` with NUnit. |
| `InitializeContext()` | Loads pending records. Safe to call repeatedly. |
| `InitializeContext(params Entity[])` | Appends records and loads them. |
| `AddUser(fullName)` | Appends a `systemuser` record with an id and returns it. |
| `SetCaller(user)` | Runs everything that follows as this user. |
| `Create`, `Update`, `Delete`, `Execute<TResponse>` | Service calls. |
| `Retrieve(logicalName, id, columns)`, `Retrieve(reference, columns)`, `Retrieve<TEntity>(id, columns)` | Retrieve one record, all columns when none are given. |
| `Reload(record)` | Current state of a record from the context. |
| `RetrieveMultiple(QueryBase)`, `RetrieveMultiple(fetchXml)` | Query results as a list. |
| `Query(logicalName)`, `Query<TEntity>()` | LINQ over a table. |
| `GetAll`, `GetAll<TEntity>`, `Single(logicalName, predicate)` | All records of a table, or exactly one matching record. |
| `Count(logicalName)`, `Exists(reference)` | Number of records in a table, and whether a record exists. |
| `CreatePluginContext(message, target, stage, preImage, postImage)` | Plugin execution context with target and images. |
| `ExecutePlugin<TPlugin>(context)`, `ExecutePlugin(instance, context)` | Run a plugin. |
| `ExecutePluginOnCreate`, `ExecutePluginOnUpdate`, `ExecutePluginOnDelete` | Build the context, run the plugin, return the context. |
| `CreateWorkflowContext(primaryEntity)` | Workflow context for a primary record. |
| `ExecuteWorkflowActivity<TActivity>(...)` | Run a custom workflow activity and return its outputs. |
| `PreImageName`, `PostImageName` | `"PreImage"`, `"PostImage"`. |
| `PreValidationStage`, `PreOperationStage`, `PostOperationStage` | `10`, `20`, `40`. |
