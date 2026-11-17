# Usage guide

Practical examples for testing plugins, custom workflow activities and other `IOrganizationService` code against Dynamics 365 CE on-premises v9.

Every snippet in this guide is taken from [`tests/FakeXrmEasy.Tests/UsageGuide/UsageGuideExamples.cs`](../tests/FakeXrmEasy.Tests/UsageGuide/UsageGuideExamples.cs), which is compiled and run with the test suite. If you change an example here, change it there too.

- [1. Set up a test project](#1-set-up-a-test-project)
- [2. The faked context](#2-the-faked-context)
- [3. Testing plugins](#3-testing-plugins)
- [4. Pipeline simulation](#4-pipeline-simulation)
- [5. Testing custom workflow activities](#5-testing-custom-workflow-activities)
- [6. Queries](#6-queries)
- [7. Built-in messages](#7-built-in-messages)
- [8. Rollup fields](#8-rollup-fields)
- [9. Custom fakes](#9-custom-fakes)
- [10. Troubleshooting](#10-troubleshooting)

## 1. Set up a test project

Plugins and workflow activities for v9 run on .NET Framework, so the test project must target .NET Framework 4.6.2 or later. An SDK-style xUnit project looks like this:

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

  <ItemGroup>
    <ProjectReference Include="..\Contoso.Plugins\Contoso.Plugins.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="System.Activities" />
  </ItemGroup>
</Project>
```

See the [README](../README.md#ci-and-releases) for adding the GitHub Packages or GitLab package source. MSTest and NUnit work the same way; FakeXrmEasy does not depend on a test framework.

Use one `XrmFakedContext` per test. It holds all records in memory, so sharing it between tests leaks state.

## 2. The faked context

### Seed data and get the service

```csharp
var context = new XrmFakedContext();
var service = context.GetOrganizationService();

var account = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso Ltd" };
var contact = new Entity("contact")
{
    Id = Guid.NewGuid(),
    ["firstname"] = "Lukas",
    ["emailaddress1"] = "lbaumdeveloper+contact@gmail.com",
    ["parentcustomerid"] = account.ToEntityReference()
};
context.Initialize(new[] { account, contact });

var retrieved = service.Retrieve("contact", contact.Id, new ColumnSet("firstname", "parentcustomerid"));

Assert.Equal("Lukas", retrieved.GetAttributeValue<string>("firstname"));
Assert.Equal(account.Id, retrieved.GetAttributeValue<EntityReference>("parentcustomerid").Id);
```

`Initialize` may be called only once per context. Records need an `Id`. Lookups are not checked against existing records unless you set `context.ValidateReferences = true`.

### Run as a specific user

`CallerId` is the user every request runs as. It sets `createdby`, `modifiedby` and the default `ownerid`, and it is the default `UserId` of plugin and workflow contexts. If you don't set it, a random user id is generated.

```csharp
var userId = Guid.NewGuid();
var context = new XrmFakedContext { CallerId = new EntityReference("systemuser", userId) };
context.Initialize(new Entity("systemuser") { Id = userId, ["fullname"] = "Test User" });
var service = context.GetOrganizationService();

var id = service.Create(new Entity("account") { ["name"] = "Fabrikam" });

var account = service.Retrieve("account", id, new ColumnSet(true));
Assert.Equal(userId, account.GetAttributeValue<EntityReference>("createdby").Id);
Assert.Equal(userId, account.GetAttributeValue<EntityReference>("ownerid").Id);
```

### Early-bound entities

Pass the assembly that contains your generated classes (CrmSvcUtil or XrmToolBox Early Bound Generator):

```csharp
var context = new XrmFakedContext();
context.EnableProxyTypes(typeof(Account).Assembly);
var service = context.GetOrganizationService();

var id = service.Create(new Account { Name = "Contoso Ltd" });

var account = service.Retrieve(Account.EntityLogicalName, id, new ColumnSet(true)).ToEntity<Account>();
Assert.Equal("Contoso Ltd", account.Name);
```

Adding an early-bound record (through `Initialize` or `Create`) enables its assembly automatically. With proxy types enabled, every entity you use must exist in the generated classes, and query conditions are compared using the generated property types.

### Inspect the in-memory store

For assertions you can read the store directly instead of going through the service:

```csharp
service.Create(new Entity("account") { ["name"] = "A" });
service.Create(new Entity("account") { ["name"] = "B" });

Assert.Equal(2, context.Data["account"].Count);
Assert.Equal(2, context.CreateQuery("account").Count());
```

## 3. Testing plugins

The examples use this plugin, registered pre-operation on Create and Update of `contact`:

```csharp
public class ContactNormalizePlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

        var target = (Entity)context.InputParameters["Target"];
        var preImage = context.PreEntityImages.Contains("PreImage") ? context.PreEntityImages["PreImage"] : null;

        var email = target.GetAttributeValue<string>("emailaddress1");
        if (email != null && !email.Contains("@"))
        {
            throw new InvalidPluginExecutionException($"'{email}' is not a valid email address.");
        }

        var firstName = target.Contains("firstname") ? target.GetAttributeValue<string>("firstname") : preImage?.GetAttributeValue<string>("firstname");
        var lastName = target.Contains("lastname") ? target.GetAttributeValue<string>("lastname") : preImage?.GetAttributeValue<string>("lastname");
        target["fullname"] = $"{firstName} {lastName}".Trim();

        tracing.Trace("Full name set to {0}", target["fullname"]);
    }
}
```

The plugin receives a service provider that returns `IPluginExecutionContext`, `IExecutionContext`, `ITracingService`, `IOrganizationServiceFactory`, `IOrganizationService` and `IServiceEndpointNotificationService`, all bound to the faked context.

### Target only

`ExecutePluginWithTarget` puts the entity into `InputParameters["Target"]` and sets `PrimaryEntityName`/`PrimaryEntityId` from it. Pre-operation plugins change the target in place, so assert on the same instance:

```csharp
var context = new XrmFakedContext();
var target = new Entity("contact") { Id = Guid.NewGuid(), ["firstname"] = "Lukas", ["lastname"] = "Baum" };

context.ExecutePluginWithTarget<ContactNormalizePlugin>(target, "Create", (int)ProcessingStepStage.Preoperation);

Assert.Equal("Lukas Baum", target["fullname"]);
```

Message and stage default to `"Create"` and `40` (post-operation).

### Full execution context with a pre-image

Start from `GetDefaultPluginContext()` and set whatever the plugin reads: message, stage, images, depth, shared variables, parent context and so on.

```csharp
var context = new XrmFakedContext();
var contactId = Guid.NewGuid();
var target = new Entity("contact", contactId) { ["lastname"] = "Smith" };
var preImage = new Entity("contact", contactId) { ["firstname"] = "Lukas", ["lastname"] = "Baum" };

var pluginContext = context.GetDefaultPluginContext();
pluginContext.MessageName = "Update";
pluginContext.Stage = (int)ProcessingStepStage.Preoperation;
pluginContext.PrimaryEntityName = "contact";
pluginContext.PrimaryEntityId = contactId;
pluginContext.InputParameters["Target"] = target;
pluginContext.PreEntityImages.Add("PreImage", preImage);

context.ExecutePluginWith<ContactNormalizePlugin>(pluginContext);

Assert.Equal("Lukas Smith", target["fullname"]);
```

Post-images go into `pluginContext.PostEntityImages` the same way. The image name must match the alias the plugin reads.

### Expected exceptions

```csharp
var context = new XrmFakedContext();
var target = new Entity("contact") { Id = Guid.NewGuid(), ["emailaddress1"] = "not-an-email" };

var ex = Assert.Throws<InvalidPluginExecutionException>(
    () => context.ExecutePluginWithTarget<ContactNormalizePlugin>(target));

Assert.Contains("not a valid email address", ex.Message);
```

### Trace output

```csharp
var context = new XrmFakedContext();
var target = new Entity("contact") { Id = Guid.NewGuid(), ["firstname"] = "Lukas", ["emailaddress1"] = "lbaumdeveloper+trace@gmail.com" };

context.ExecutePluginWithTarget<ContactNormalizePlugin>(target);

Assert.Contains("Full name set to Lukas", context.GetFakeTracingService().DumpTrace());
```

The trace collects everything written during the context's lifetime, from all plugins and workflow activities.

### Plugins that write data

A post-operation plugin that creates a follow-up task:

```csharp
public class AccountFollowUpPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        var service = factory.CreateOrganizationService(context.UserId);

        var account = (Entity)context.InputParameters["Target"];
        service.Create(new Entity("task")
        {
            ["subject"] = $"Welcome call for {account.GetAttributeValue<string>("name")}",
            ["regardingobjectid"] = account.ToEntityReference(),
            ["scheduledend"] = DateTime.UtcNow.AddDays(7)
        });
    }
}
```

```csharp
var context = new XrmFakedContext();
var account = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso Ltd" };

context.ExecutePluginWithTarget<AccountFollowUpPlugin>(account, "Create", (int)ProcessingStepStage.Postoperation);

var task = context.CreateQuery("task").Single();
Assert.Equal("Welcome call for Contoso Ltd", task["subject"]);
Assert.Equal(account.Id, task.GetAttributeValue<EntityReference>("regardingobjectid").Id);
```

### Delete and other EntityReference targets

Use `ExecutePluginWithTargetReference` when the target is an `EntityReference`. Here the plugin blocks deleting an account that still has open opportunities:

```csharp
var context = new XrmFakedContext();
var account = new Entity("account") { Id = Guid.NewGuid() };
context.Initialize(new[]
{
    account,
    new Entity("opportunity")
    {
        Id = Guid.NewGuid(),
        ["parentaccountid"] = account.ToEntityReference(),
        ["statecode"] = new OptionSetValue(0)
    }
});

Assert.Throws<InvalidPluginExecutionException>(() =>
    context.ExecutePluginWithTargetReference<PreventAccountDeletePlugin>(account.ToEntityReference(), "Delete", (int)ProcessingStepStage.Prevalidation));
```

### Unsecure and secure configuration

Plugins with a `(string unsecureConfiguration, string secureConfiguration)` constructor:

```csharp
var context = new XrmFakedContext();
var target = new Entity("account") { Id = Guid.NewGuid() };
var pluginContext = context.GetDefaultPluginContext();
pluginContext.InputParameters["Target"] = target;

context.ExecutePluginWithConfigurations<DefaultCreditLimitPlugin>(pluginContext, "2500", null);

Assert.Equal(2500m, target.GetAttributeValue<Money>("creditlimit").Value);
```

If your plugin needs constructor dependencies, create the instance yourself and call `context.ExecutePluginWith(pluginContext, instance)`.

## 4. Pipeline simulation

Executing a plugin directly tests it in isolation. Pipeline simulation instead runs registered steps when your code calls `Create`, `Update` or `Delete` on the service, which is useful for testing plugin chains or code that relies on plugins.

Steps for a specific entity need early-bound classes, because the step filter uses the class's `EntityTypeCode`. Once the proxy types assembly is known (`RegisterPluginStep<TPlugin, TEntity>` sets it), late-bound `Entity` targets match those steps too.

```csharp
var context = new XrmFakedContext { UsePipelineSimulation = true };
context.EnableProxyTypes(typeof(Account).Assembly);
context.RegisterPluginStep<AccountFollowUpPlugin, Account>("Create", ProcessingStepStage.Postoperation);
var service = context.GetOrganizationService();

service.Create(new Account { Name = "Contoso Ltd" });

Assert.Single(context.CreateQuery("task"));
```

### Images and filtering attributes

Register images with `PluginStepImage(name, type, attributes...)`. Leave out the attributes to include all columns. Filtering attributes work as on the server: the step runs only if the target contains at least one of them.

```csharp
var context = new XrmFakedContext { UsePipelineSimulation = true };
context.EnableProxyTypes(typeof(Contact).Assembly);
var contactId = Guid.NewGuid();
context.Initialize(new Contact { Id = contactId, FirstName = "Lukas", LastName = "Baum" });

context.RegisterPluginStep<ContactNormalizePlugin, Contact>("Update", ProcessingStepStage.Preoperation,
    filteringAttributes: new[] { "firstname", "lastname" },
    images: new[] { new PluginStepImage("PreImage", ProcessingStepImageType.PreImage, "firstname", "lastname") });
var service = context.GetOrganizationService();

service.Update(new Contact { Id = contactId, LastName = "Smith" });
service.Update(new Contact { Id = contactId, JobTitle = "Developer" });

var contact = service.Retrieve(Contact.EntityLogicalName, contactId, new ColumnSet("fullname")).ToEntity<Contact>();
Assert.Equal("Lukas Smith", contact.FullName);
```

Which images a stage receives:

| Message | Pre-image | Post-image |
|---------|-----------|------------|
| Create | no | post-operation only |
| Update | all stages | post-operation only |
| Delete | all stages | no |

Other optional arguments of `RegisterPluginStep` are `mode` (synchronous or asynchronous) and `rank` (execution order within a stage). Asynchronous steps run immediately after the synchronous post-operation steps. Use `RegisterPluginStep<TPlugin>(message, ..., primaryEntityTypeCode: null)` to register a step for all entities.

### Stages, nesting and infinite loops

Pre-validation, pre-operation and post-operation steps run in that order. When a plugin's own service call triggers another step, that step gets `Depth` + 1, the calling plugin's context as `ParentContext`, and the same `CorrelationId`, so depth guards can be tested:

```csharp
var context = new XrmFakedContext { UsePipelineSimulation = true };
context.RegisterPluginStep<CreateContactForAccountPlugin, Account>("Create");
context.RegisterPluginStep<ContactPlugin, Contact>("Create");

context.GetOrganizationService().Create(new Account());
// ContactPlugin runs with Depth == 2 and ParentContext.MessageName == "Create"
```

More than `MaxPipelineDepth` nested executions (default 8) throw the infinite loop `FaultException<OrganizationServiceFault>`. Exceptions thrown by a registered step reach the caller unchanged.

## 5. Testing custom workflow activities

```csharp
public class CreatePhoneCallActivity : CodeActivity
{
    [Input("Subject")]
    [RequiredArgument]
    public InArgument<string> Subject { get; set; }

    [Input("Due in days")]
    [Default("3")]
    public InArgument<int> DueInDays { get; set; }

    [Output("Phone call")]
    [ReferenceTarget("phonecall")]
    public OutArgument<EntityReference> PhoneCall { get; set; }

    protected override void Execute(CodeActivityContext executionContext)
    {
        var context = executionContext.GetExtension<IWorkflowContext>();
        var service = executionContext.GetExtension<IOrganizationServiceFactory>().CreateOrganizationService(context.UserId);

        var id = service.Create(new Entity("phonecall")
        {
            ["subject"] = Subject.Get(executionContext),
            ["regardingobjectid"] = new EntityReference(context.PrimaryEntityName, context.PrimaryEntityId),
            ["scheduledend"] = DateTime.UtcNow.Date.AddDays(DueInDays.Get(executionContext))
        });

        PhoneCall.Set(executionContext, new EntityReference("phonecall", id));
    }
}
```

Inputs are passed by property name, outputs come back by property name:

```csharp
var context = new XrmFakedContext();
var account = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso Ltd" };
context.Initialize(account);

var inputs = new Dictionary<string, object>
{
    { "Subject", "Discuss renewal" },
    { "DueInDays", 5 }
};

var outputs = context.ExecuteCodeActivity<CreatePhoneCallActivity>(account, inputs);

var phoneCallRef = (EntityReference)outputs["PhoneCall"];
var phoneCall = context.GetOrganizationService().Retrieve("phonecall", phoneCallRef.Id, new ColumnSet(true));
Assert.Equal("Discuss renewal", phoneCall["subject"]);
Assert.Equal(account.Id, phoneCall.GetAttributeValue<EntityReference>("regardingobjectid").Id);
```

`[Default]` is only read by the CRM process designer. In a test, an input you don't pass has the type's default value (`0` for `DueInDays`), so pass every input the activity relies on.

### Custom workflow context

`GetDefaultWorkflowContext()` returns a context you can change before running the activity:

```csharp
var userId = Guid.NewGuid();
var context = new XrmFakedContext { CallerId = new EntityReference("systemuser", userId) };
var account = new Entity("account") { Id = Guid.NewGuid() };
context.Initialize(account);

var workflowContext = context.GetDefaultWorkflowContext();
workflowContext.MessageName = "Update";
workflowContext.PrimaryEntityName = "account";
workflowContext.PrimaryEntityId = account.Id;

context.ExecuteCodeActivity<CreatePhoneCallActivity>(workflowContext,
    new Dictionary<string, object> { { "Subject", "Check data" }, { "DueInDays", 1 } });

var phoneCall = context.CreateQuery("phonecall").Single();
Assert.Equal(userId, workflowContext.UserId);
Assert.Equal(userId, phoneCall.GetAttributeValue<EntityReference>("ownerid").Id);
```

`CreateOrganizationService(userId)` always returns the context's service, which runs as `context.CallerId`. To test code that impersonates a user, set `CallerId` to that user, not only the context's `UserId`.

## 6. Queries

The examples below use this data:

```csharp
var contoso = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso Ltd", ["address1_city"] = "Berlin" };
var fabrikam = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Fabrikam", ["address1_city"] = "Munich" };

context.Initialize(new[]
{
    contoso,
    fabrikam,
    new Entity("contact") { Id = Guid.NewGuid(), ["lastname"] = "Baum", ["parentcustomerid"] = contoso.ToEntityReference(), ["emailaddress1"] = "lbaumdeveloper+baum@gmail.com" },
    new Entity("contact") { Id = Guid.NewGuid(), ["lastname"] = "Meyer", ["parentcustomerid"] = contoso.ToEntityReference() },
    new Entity("contact") { Id = Guid.NewGuid(), ["lastname"] = "Schulz", ["parentcustomerid"] = fabrikam.ToEntityReference() },
    new Entity("opportunity") { Id = Guid.NewGuid(), ["parentaccountid"] = contoso.ToEntityReference(), ["estimatedvalue"] = new Money(1000m) },
    new Entity("opportunity") { Id = Guid.NewGuid(), ["parentaccountid"] = contoso.ToEntityReference(), ["estimatedvalue"] = new Money(2500m) }
});
```

### QueryExpression with a link entity

```csharp
var query = new QueryExpression("contact")
{
    ColumnSet = new ColumnSet("lastname"),
    Orders = { new OrderExpression("lastname", OrderType.Ascending) }
};
var account = query.AddLink("account", "parentcustomerid", "accountid");
account.EntityAlias = "acc";
account.Columns = new ColumnSet("name");
account.LinkCriteria.AddCondition("address1_city", ConditionOperator.Equal, "Berlin");

var contacts = service.RetrieveMultiple(query).Entities;

Assert.Equal(new[] { "Baum", "Meyer" }, contacts.Select(c => c.GetAttributeValue<string>("lastname")));
Assert.Equal("Contoso Ltd", contacts[0].GetAttributeValue<AliasedValue>("acc.name").Value);
```

### FetchXML

```csharp
var fetchXml = @"
<fetch>
  <entity name='contact'>
    <attribute name='lastname' />
    <filter>
      <condition attribute='emailaddress1' operator='like' value='%@gmail.com' />
    </filter>
  </entity>
</fetch>";

var contacts = service.RetrieveMultiple(new FetchExpression(fetchXml)).Entities;

Assert.Equal("Baum", contacts.Single()["lastname"]);
```

Aggregates (`count`, `countcolumn`, `sum`, `avg`, `min`, `max`) and `groupby` are supported:

```csharp
var fetchXml = $@"
<fetch aggregate='true'>
  <entity name='opportunity'>
    <attribute name='estimatedvalue' alias='total' aggregate='sum' />
    <filter>
      <condition attribute='parentaccountid' operator='eq' value='{contoso.Id}' />
    </filter>
  </entity>
</fetch>";

var result = service.RetrieveMultiple(new FetchExpression(fetchXml)).Entities.Single();

Assert.Equal(3500m, ((Money)result.GetAttributeValue<AliasedValue>("total").Value).Value);
```

### LINQ

`context.CreateQuery` gives you an `IQueryable` over the store, late-bound or early-bound:

```csharp
var names = (from c in context.CreateQuery("contact")
             join a in context.CreateQuery("account") on c.GetAttributeValue<EntityReference>("parentcustomerid").Id equals a.Id
             where a.GetAttributeValue<string>("address1_city") == "Berlin"
             orderby c.GetAttributeValue<string>("lastname")
             select c.GetAttributeValue<string>("lastname")).ToList();

Assert.Equal(new[] { "Baum", "Meyer" }, names);
```

```csharp
var context = new XrmFakedContext();
context.Initialize(new[]
{
    new Account { Id = Guid.NewGuid(), Name = "Contoso Ltd", NumberOfEmployees = 250 },
    new Account { Id = Guid.NewGuid(), Name = "Fabrikam", NumberOfEmployees = 12 }
});

var large = context.CreateQuery<Account>().Where(a => a.NumberOfEmployees > 100).Select(a => a.Name).ToList();

Assert.Equal(new[] { "Contoso Ltd" }, large);
```

Code under test that uses an `OrganizationServiceContext` (for example a CrmSvcUtil service context) also works: create it with `context.GetOrganizationService()`.

### User and team operators

`EqualUserId`, `NotEqualUserId`, `EqualUserTeams` and `EqualUserOrUserTeams` resolve against `CallerId`. Team membership comes from `teammembership` records:

```csharp
var userId = Guid.NewGuid();
var team = new Entity("team") { Id = Guid.NewGuid() };
var context = new XrmFakedContext { CallerId = new EntityReference("systemuser", userId) };
context.Initialize(new[]
{
    team,
    new Entity("teammembership") { Id = Guid.NewGuid(), ["teamid"] = team.Id, ["systemuserid"] = userId },
    new Entity("account") { Id = Guid.NewGuid(), ["ownerid"] = new EntityReference("systemuser", userId) },
    new Entity("account") { Id = Guid.NewGuid(), ["ownerid"] = team.ToEntityReference() },
    new Entity("account") { Id = Guid.NewGuid(), ["ownerid"] = new EntityReference("systemuser", Guid.NewGuid()) }
});

var query = new QueryExpression("account");
query.Criteria.AddCondition("ownerid", ConditionOperator.EqualUserOrUserTeams);

Assert.Equal(2, context.GetOrganizationService().RetrieveMultiple(query).Entities.Count);
```

## 7. Built-in messages

`service.Execute` is handled by fake message executors. The implemented requests are in [`src/FakeXrmEasy/FakeMessageExecutors`](../src/FakeXrmEasy/FakeMessageExecutors), among them `Create`, `Update`, `Delete`, `Retrieve`, `RetrieveMultiple`, `Upsert`, `Assign`, `SetState`, `Associate`, `Disassociate`, `ExecuteMultiple`, `ExecuteTransaction`, `WhoAmI`, `SendEmail`, `QualifyLead`, `WinOpportunity`, `LoseOpportunity`, `CloseIncident`, `InitializeFrom`, `AddToQueue`, `PickFromQueue`, `AddMembersTeam`, `GrantAccess`, `ModifyAccess`, `RevokeAccess`, `RetrievePrincipalAccess`, `RetrieveEntity`, `RetrieveAttribute`, `RetrieveOptionSet`, `CalculateRollupField` and `RetrieveVersion`.

### SetState and Assign

```csharp
var context = new XrmFakedContext();
var account = new Entity("account") { Id = Guid.NewGuid() };
var newOwner = new EntityReference("systemuser", Guid.NewGuid());
context.Initialize(account);
var service = context.GetOrganizationService();

service.Execute(new SetStateRequest
{
    EntityMoniker = account.ToEntityReference(),
    State = new OptionSetValue(1),
    Status = new OptionSetValue(2)
});
service.Execute(new AssignRequest { Target = account.ToEntityReference(), Assignee = newOwner });

var updated = service.Retrieve("account", account.Id, new ColumnSet("statecode", "ownerid"));
Assert.Equal(1, updated.GetAttributeValue<OptionSetValue>("statecode").Value);
Assert.Equal(newOwner.Id, updated.GetAttributeValue<EntityReference>("ownerid").Id);
```

### SendEmail

No email leaves the test. The email record is set to Completed/Sent (`statecode` 1, `statuscode` 3):

```csharp
var context = new XrmFakedContext();
var contact = new Entity("contact") { Id = Guid.NewGuid(), ["emailaddress1"] = "lbaumdeveloper+invoice@gmail.com" };
var email = new Entity("email")
{
    Id = Guid.NewGuid(),
    ["subject"] = "Your invoice",
    ["to"] = new EntityCollection(new[] { new Entity("activityparty") { ["partyid"] = contact.ToEntityReference() } })
};
context.Initialize(new[] { contact, email });
var service = context.GetOrganizationService();

service.Execute(new SendEmailRequest { EmailId = email.Id, IssueSend = true, TrackingToken = string.Empty });

Assert.Equal(3, service.Retrieve("email", email.Id, new ColumnSet("statuscode")).GetAttributeValue<OptionSetValue>("statuscode").Value);
```

### N:N relationships

Many-to-many relationships must be described once per context, because there is no metadata to read them from:

```csharp
var context = new XrmFakedContext();
context.AddRelationship("accountleads_association", new XrmFakedRelationship
{
    IntersectEntity = "accountleads",
    Entity1LogicalName = "account",
    Entity1Attribute = "accountid",
    Entity2LogicalName = "lead",
    Entity2Attribute = "leadid"
});
var account = new Entity("account") { Id = Guid.NewGuid() };
var lead = new Entity("lead") { Id = Guid.NewGuid() };
context.Initialize(new[] { account, lead });
var service = context.GetOrganizationService();

service.Associate("account", account.Id, new Relationship("accountleads_association"),
    new EntityReferenceCollection { lead.ToEntityReference() });

var link = context.CreateQuery("accountleads").Single();
Assert.Equal(lead.Id, link.GetAttributeValue<Guid>("leadid"));
```

### ExecuteMultiple

```csharp
var request = new ExecuteMultipleRequest
{
    Settings = new ExecuteMultipleSettings { ContinueOnError = false, ReturnResponses = true },
    Requests = new OrganizationRequestCollection
    {
        new CreateRequest { Target = new Entity("account") { ["name"] = "A" } },
        new CreateRequest { Target = new Entity("account") { ["name"] = "B" } }
    }
};

var response = (ExecuteMultipleResponse)service.Execute(request);

Assert.False(response.IsFaulted);
Assert.Equal(2, response.Responses.Count);
Assert.Equal(2, context.CreateQuery("account").Count());
```

### WhoAmI

```csharp
var userId = Guid.NewGuid();
var context = new XrmFakedContext { CallerId = new EntityReference("systemuser", userId) };

var response = (WhoAmIResponse)context.GetOrganizationService().Execute(new WhoAmIRequest());

Assert.Equal(userId, response.UserId);
```

`BusinessUnitId` and `OrganizationId` are only returned when a matching `systemuser` record (with `businessunitid`) exists.

### Merge

`MergeRequest` works for `account`, `contact`, `lead` and `incident`. It copies `UpdateContent` to the master, points every lookup that referenced the subordinate to the master, and deactivates the subordinate with `merged` and `masterid` set.

```csharp
service.Execute(new MergeRequest
{
    Target = new EntityReference("account", masterId),
    SubordinateId = duplicateId,
    UpdateContent = new Entity("account") { ["telephone1"] = "123" }
});
```

### ExecuteWorkflow

Each `ExecuteWorkflowRequest` is recorded in `context.ExecutedWorkflowRequests` and creates an `asyncoperation`. Register a workflow to also simulate what it does; a failure marks the job as failed instead of throwing, like the asynchronous server job.

```csharp
context.RegisterWorkflow(workflowId, (service, request) =>
    service.Update(new Entity("account") { Id = request.EntityId, ["name"] = "Processed" }));

service.Execute(new ExecuteWorkflowRequest { WorkflowId = workflowId, EntityId = accountId });

Assert.Single(context.ExecutedWorkflowRequests);
```

## 8. Rollup fields

Rollup definitions live in CRM metadata, which the context doesn't have. Register the calculation per field, and `CalculateRollupFieldRequest` calculates and stores the value:

```csharp
var context = new XrmFakedContext();
var account = new Entity("account") { Id = Guid.NewGuid() };
context.Initialize(new[]
{
    account,
    new Entity("opportunity") { Id = Guid.NewGuid(), ["parentaccountid"] = account.ToEntityReference(), ["estimatedvalue"] = new Money(1000m) },
    new Entity("opportunity") { Id = Guid.NewGuid(), ["parentaccountid"] = account.ToEntityReference(), ["estimatedvalue"] = new Money(2500m) }
});
context.RegisterRollupField("account", "new_openrevenue",
    RollupFields.Sum("opportunity", "parentaccountid", "estimatedvalue"));
var service = context.GetOrganizationService();

var response = (CalculateRollupFieldResponse)service.Execute(new CalculateRollupFieldRequest
{
    Target = account.ToEntityReference(),
    FieldName = "new_openrevenue"
});

Assert.Equal(3500m, ((Money)response.Entity["new_openrevenue"]).Value);
```

`RollupFields` provides `Count`, `Sum`, `Avg`, `Min` and `Max`, each with an optional `FilterExpression` for the related records. For anything else pass your own `(service, target) => value` function.

## 9. Custom fakes

Use custom fakes for custom actions, for requests that aren't implemented yet, or to force a specific response.

### Custom actions with RegisterCustomAction

The shortest way to fake a custom action is a handler from input to output parameters. It also matches generated early-bound request classes by their request name.

```csharp
context.RegisterCustomAction("new_CalculateDiscount", inputs => new ParameterCollection
{
    { "Discount", (decimal)inputs["Amount"] * 0.1m }
});
```

Use the overload `(service, request) => outputs` when the action needs to read or write data.

### Custom actions with a message executor

A plugin that calls the custom action `new_CalculateDiscount`:

```csharp
public class ApplyDiscountPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory)))
            .CreateOrganizationService(context.UserId);

        var target = (Entity)context.InputParameters["Target"];
        var response = service.Execute(new OrganizationRequest("new_CalculateDiscount")
        {
            ["AccountId"] = target.GetAttributeValue<EntityReference>("customerid")
        });

        target["discountpercentage"] = (decimal)response["Discount"];
    }
}
```

Implement `IFakeMessageExecutor` with the behaviour the test needs. The executor gets the faked context, so it can read and write records:

```csharp
public class CalculateDiscountExecutor : IFakeMessageExecutor
{
    public bool CanExecute(OrganizationRequest request) => request.RequestName == "new_CalculateDiscount";

    public Type GetResponsibleRequestType() => typeof(OrganizationRequest);

    public OrganizationResponse Execute(OrganizationRequest request, XrmFakedContext ctx)
    {
        var accountRef = (EntityReference)request["AccountId"];
        var account = ctx.GetOrganizationService().Retrieve("account", accountRef.Id, new ColumnSet("numberofemployees"));

        return new OrganizationResponse
        {
            ResponseName = "new_CalculateDiscount",
            Results = new ParameterCollection { { "Discount", account.GetAttributeValue<int>("numberofemployees") > 100 ? 10m : 0m } }
        };
    }
}
```

Register it by message name with `AddGenericFakeMessageExecutor`:

```csharp
var context = new XrmFakedContext();
context.AddGenericFakeMessageExecutor("new_CalculateDiscount", new CalculateDiscountExecutor());
var account = new Entity("account") { Id = Guid.NewGuid(), ["numberofemployees"] = 250 };
context.Initialize(account);
var order = new Entity("salesorder") { Id = Guid.NewGuid(), ["customerid"] = account.ToEntityReference() };

context.ExecutePluginWithTarget<ApplyDiscountPlugin>(order);

Assert.Equal(10m, order["discountpercentage"]);
```

For early-bound request classes (for example generated custom action requests or SDK requests), use `context.AddFakeMessageExecutor<TRequest>(executor)` instead. It also replaces a built-in executor for that request type.

### One-off responses with an execution mock

When a test only needs a fixed response, a delegate is shorter than an executor. Execution mocks take precedence over all executors:

```csharp
var context = new XrmFakedContext();
context.AddExecutionMock<RetrieveVersionRequest>(request => new RetrieveVersionResponse
{
    Results = new ParameterCollection { { "Version", "9.1.0.1234" } }
});

var response = (RetrieveVersionResponse)context.GetOrganizationService().Execute(new RetrieveVersionRequest());

Assert.Equal("9.1.0.1234", response.Version);
```

Remove fakes with `RemoveExecutionMock<T>()`, `RemoveFakeMessageExecutor<T>()` or `RemoveGenericFakeMessageExecutor(name)`.

## 10. Troubleshooting

**`PullRequestException`**: the request has no fake implementation. Add a custom fake (section 9) or implement the executor in this repository.

```csharp
Assert.Throws<PullRequestException>(() =>
    context.GetOrganizationService().Execute(new RetrieveAllEntitiesRequest()));
```

**A registered pipeline step doesn't run**: check that `UsePipelineSimulation` is `true`, that the step is registered for an early-bound entity class and a simulated stage (pre-operation or post-operation), and that the target contains one of the filtering attributes. Records added with `Initialize` never trigger steps.

**Plugin changes are not visible through the service**: `ExecutePluginWithTarget` only runs the plugin. It does not save the target. Assert on the target instance, or use pipeline simulation if you want the full Create/Update to happen.

**`TypeLoadException` when running a workflow activity**: the test project needs a reference to `System.Activities` and must target .NET Framework.

**`Entity ... does not exist in the metadata cache`**: proxy types are enabled and the entity has no generated class. Regenerate the early-bound classes after adding entities in CRM, or use only late-bound entities in that test.
