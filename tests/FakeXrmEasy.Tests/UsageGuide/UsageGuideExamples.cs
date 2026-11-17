using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using Crm;
using FakeXrmEasy.FakeMessageExecutors;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Xunit;

namespace FakeXrmEasy.Tests.UsageGuide
{
    /// <summary>
    /// Pre-operation plugin on Create/Update of contact. Validates the email and derives the full name.
    /// </summary>
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

    /// <summary>
    /// Post-operation plugin on Create of account. Creates a follow-up task regarding the new account.
    /// </summary>
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

    /// <summary>
    /// Pre-validation plugin on Delete of account. Blocks deletion while open opportunities exist.
    /// </summary>
    public class PreventAccountDeletePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var service = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory)))
                .CreateOrganizationService(context.UserId);

            var target = (EntityReference)context.InputParameters["Target"];
            var query = new QueryExpression("opportunity")
            {
                ColumnSet = new ColumnSet(false),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("parentaccountid", ConditionOperator.Equal, target.Id),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };

            if (service.RetrieveMultiple(query).Entities.Any())
            {
                throw new InvalidPluginExecutionException("The account still has open opportunities.");
            }
        }
    }

    /// <summary>
    /// Plugin reading its unsecure configuration as a default credit limit.
    /// </summary>
    public class DefaultCreditLimitPlugin : IPlugin
    {
        private readonly decimal _defaultLimit;

        public DefaultCreditLimitPlugin(string unsecureConfiguration, string secureConfiguration)
        {
            _defaultLimit = decimal.Parse(unsecureConfiguration, System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var target = (Entity)context.InputParameters["Target"];
            if (!target.Contains("creditlimit"))
            {
                target["creditlimit"] = new Money(_defaultLimit);
            }
        }
    }

    /// <summary>
    /// Plugin calling the custom action new_CalculateDiscount and storing its result.
    /// </summary>
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

    /// <summary>
    /// Custom workflow activity creating a phone call for the primary entity and returning it.
    /// </summary>
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

    public class ContextSetupTests
    {
        [Fact]
        public void Initialize_seeds_records_and_service_reads_them()
        {
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
        }

        [Fact]
        public void CallerId_is_stamped_on_created_records()
        {
            var userId = Guid.NewGuid();
            var context = new XrmFakedContext { CallerId = new EntityReference("systemuser", userId) };
            context.Initialize(new Entity("systemuser") { Id = userId, ["fullname"] = "Test User" });
            var service = context.GetOrganizationService();

            var id = service.Create(new Entity("account") { ["name"] = "Fabrikam" });

            var account = service.Retrieve("account", id, new ColumnSet(true));
            Assert.Equal(userId, account.GetAttributeValue<EntityReference>("createdby").Id);
            Assert.Equal(userId, account.GetAttributeValue<EntityReference>("ownerid").Id);
        }

        [Fact]
        public void Early_bound_entities_work_after_enabling_proxy_types()
        {
            var context = new XrmFakedContext();
            context.EnableProxyTypes(typeof(Account).Assembly);
            var service = context.GetOrganizationService();

            var id = service.Create(new Account { Name = "Contoso Ltd" });

            var account = service.Retrieve(Account.EntityLogicalName, id, new ColumnSet(true)).ToEntity<Account>();
            Assert.Equal("Contoso Ltd", account.Name);
        }

        [Fact]
        public void Data_dictionary_exposes_the_in_memory_store()
        {
            var context = new XrmFakedContext();
            var service = context.GetOrganizationService();

            service.Create(new Entity("account") { ["name"] = "A" });
            service.Create(new Entity("account") { ["name"] = "B" });

            Assert.Equal(2, context.Data["account"].Count);
            Assert.Equal(2, context.CreateQuery("account").Count());
        }
    }

    public class ContactNormalizePluginTests
    {
        [Fact]
        public void Create_sets_full_name()
        {
            var context = new XrmFakedContext();
            var target = new Entity("contact") { Id = Guid.NewGuid(), ["firstname"] = "Lukas", ["lastname"] = "Baum" };

            context.ExecutePluginWithTarget<ContactNormalizePlugin>(target, "Create", (int)ProcessingStepStage.Preoperation);

            Assert.Equal("Lukas Baum", target["fullname"]);
        }

        [Fact]
        public void Update_uses_pre_image_for_missing_attributes()
        {
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
        }

        [Fact]
        public void Invalid_email_throws()
        {
            var context = new XrmFakedContext();
            var target = new Entity("contact") { Id = Guid.NewGuid(), ["emailaddress1"] = "not-an-email" };

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => context.ExecutePluginWithTarget<ContactNormalizePlugin>(target));

            Assert.Contains("not a valid email address", ex.Message);
        }

        [Fact]
        public void Trace_output_can_be_asserted()
        {
            var context = new XrmFakedContext();
            var target = new Entity("contact") { Id = Guid.NewGuid(), ["firstname"] = "Lukas", ["emailaddress1"] = "lbaumdeveloper+trace@gmail.com" };

            context.ExecutePluginWithTarget<ContactNormalizePlugin>(target);

            Assert.Contains("Full name set to Lukas", context.GetFakeTracingService().DumpTrace());
        }
    }

    public class AccountPluginTests
    {
        [Fact]
        public void Post_create_creates_follow_up_task()
        {
            var context = new XrmFakedContext();
            var account = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso Ltd" };

            context.ExecutePluginWithTarget<AccountFollowUpPlugin>(account, "Create", (int)ProcessingStepStage.Postoperation);

            var task = context.CreateQuery("task").Single();
            Assert.Equal("Welcome call for Contoso Ltd", task["subject"]);
            Assert.Equal(account.Id, task.GetAttributeValue<EntityReference>("regardingobjectid").Id);
        }

        [Fact]
        public void Delete_is_blocked_by_open_opportunity()
        {
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
        }

        [Fact]
        public void Unsecure_configuration_is_passed_to_constructor()
        {
            var context = new XrmFakedContext();
            var target = new Entity("account") { Id = Guid.NewGuid() };
            var pluginContext = context.GetDefaultPluginContext();
            pluginContext.InputParameters["Target"] = target;

            context.ExecutePluginWithConfigurations<DefaultCreditLimitPlugin>(pluginContext, "2500", null);

            Assert.Equal(2500m, target.GetAttributeValue<Money>("creditlimit").Value);
        }
    }

    public class PipelineSimulationTests
    {
        [Fact]
        public void Registered_post_create_step_runs_on_service_create()
        {
            var context = new XrmFakedContext { UsePipelineSimulation = true };
            context.EnableProxyTypes(typeof(Account).Assembly);
            context.RegisterPluginStep<AccountFollowUpPlugin, Account>("Create", ProcessingStepStage.Postoperation);
            var service = context.GetOrganizationService();

            service.Create(new Account { Name = "Contoso Ltd" });

            Assert.Single(context.CreateQuery("task"));
        }

        [Fact]
        public void Update_step_gets_pre_image_and_honours_filtering_attributes()
        {
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
        }
    }

    public class CreatePhoneCallActivityTests
    {
        [Fact]
        public void Creates_phone_call_regarding_primary_entity()
        {
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
            Assert.Equal(DateTime.UtcNow.Date.AddDays(5), phoneCall.GetAttributeValue<DateTime>("scheduledend"));
        }

        [Fact]
        public void Custom_workflow_context_runs_as_caller()
        {
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
        }
    }

    public class QueryTests
    {
        private readonly XrmFakedContext _context = new XrmFakedContext();
        private readonly IOrganizationService _service;
        private readonly Entity _contoso;

        public QueryTests()
        {
            _service = _context.GetOrganizationService();
            _contoso = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Contoso Ltd", ["address1_city"] = "Berlin" };
            var fabrikam = new Entity("account") { Id = Guid.NewGuid(), ["name"] = "Fabrikam", ["address1_city"] = "Munich" };

            _context.Initialize(new[]
            {
                _contoso,
                fabrikam,
                new Entity("contact") { Id = Guid.NewGuid(), ["lastname"] = "Baum", ["parentcustomerid"] = _contoso.ToEntityReference(), ["emailaddress1"] = "lbaumdeveloper+baum@gmail.com" },
                new Entity("contact") { Id = Guid.NewGuid(), ["lastname"] = "Meyer", ["parentcustomerid"] = _contoso.ToEntityReference() },
                new Entity("contact") { Id = Guid.NewGuid(), ["lastname"] = "Schulz", ["parentcustomerid"] = fabrikam.ToEntityReference() },
                new Entity("opportunity") { Id = Guid.NewGuid(), ["parentaccountid"] = _contoso.ToEntityReference(), ["estimatedvalue"] = new Money(1000m) },
                new Entity("opportunity") { Id = Guid.NewGuid(), ["parentaccountid"] = _contoso.ToEntityReference(), ["estimatedvalue"] = new Money(2500m) }
            });
        }

        [Fact]
        public void QueryExpression_with_link_entity()
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("lastname"),
                Orders = { new OrderExpression("lastname", OrderType.Ascending) }
            };
            var account = query.AddLink("account", "parentcustomerid", "accountid");
            account.EntityAlias = "acc";
            account.Columns = new ColumnSet("name");
            account.LinkCriteria.AddCondition("address1_city", ConditionOperator.Equal, "Berlin");

            var contacts = _service.RetrieveMultiple(query).Entities;

            Assert.Equal(new[] { "Baum", "Meyer" }, contacts.Select(c => c.GetAttributeValue<string>("lastname")));
            Assert.Equal("Contoso Ltd", contacts[0].GetAttributeValue<AliasedValue>("acc.name").Value);
        }

        [Fact]
        public void FetchXml_query()
        {
            var fetchXml = @"
<fetch>
  <entity name='contact'>
    <attribute name='lastname' />
    <filter>
      <condition attribute='emailaddress1' operator='like' value='%@gmail.com' />
    </filter>
  </entity>
</fetch>";

            var contacts = _service.RetrieveMultiple(new FetchExpression(fetchXml)).Entities;

            Assert.Equal("Baum", contacts.Single()["lastname"]);
        }

        [Fact]
        public void FetchXml_aggregate()
        {
            var fetchXml = $@"
<fetch aggregate='true'>
  <entity name='opportunity'>
    <attribute name='estimatedvalue' alias='total' aggregate='sum' />
    <filter>
      <condition attribute='parentaccountid' operator='eq' value='{_contoso.Id}' />
    </filter>
  </entity>
</fetch>";

            var result = _service.RetrieveMultiple(new FetchExpression(fetchXml)).Entities.Single();

            Assert.Equal(3500m, ((Money)result.GetAttributeValue<AliasedValue>("total").Value).Value);
        }

        [Fact]
        public void Linq_query()
        {
            var names = (from c in _context.CreateQuery("contact")
                         join a in _context.CreateQuery("account") on c.GetAttributeValue<EntityReference>("parentcustomerid").Id equals a.Id
                         where a.GetAttributeValue<string>("address1_city") == "Berlin"
                         orderby c.GetAttributeValue<string>("lastname")
                         select c.GetAttributeValue<string>("lastname")).ToList();

            Assert.Equal(new[] { "Baum", "Meyer" }, names);
        }

        [Fact]
        public void Linq_query_early_bound()
        {
            var context = new XrmFakedContext();
            context.Initialize(new[]
            {
                new Account { Id = Guid.NewGuid(), Name = "Contoso Ltd", NumberOfEmployees = 250 },
                new Account { Id = Guid.NewGuid(), Name = "Fabrikam", NumberOfEmployees = 12 }
            });

            var large = context.CreateQuery<Account>().Where(a => a.NumberOfEmployees > 100).Select(a => a.Name).ToList();

            Assert.Equal(new[] { "Contoso Ltd" }, large);
        }

        [Fact]
        public void EqualUserOrUserTeams_operator()
        {
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
        }
    }

    public class MessageTests
    {
        [Fact]
        public void SetState_and_Assign()
        {
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
        }

        [Fact]
        public void SendEmail_marks_email_as_sent()
        {
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
        }

        [Fact]
        public void Associate_many_to_many()
        {
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
        }

        [Fact]
        public void ExecuteMultiple_returns_responses()
        {
            var context = new XrmFakedContext();
            var service = context.GetOrganizationService();

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
        }

        [Fact]
        public void WhoAmI_returns_caller()
        {
            var userId = Guid.NewGuid();
            var context = new XrmFakedContext { CallerId = new EntityReference("systemuser", userId) };

            var response = (WhoAmIResponse)context.GetOrganizationService().Execute(new WhoAmIRequest());

            Assert.Equal(userId, response.UserId);
        }

        [Fact]
        public void Rollup_field_is_calculated()
        {
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
        }
    }

    /// <summary>
    /// Fake for the custom action new_CalculateDiscount: 10 percent for accounts with more than 100 employees, otherwise 0.
    /// </summary>
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

    public class CustomFakeTests
    {
        [Fact]
        public void Execution_mock_for_one_test()
        {
            var context = new XrmFakedContext();
            context.AddGenericFakeMessageExecutor("new_CalculateDiscount", new CalculateDiscountExecutor());
            var account = new Entity("account") { Id = Guid.NewGuid(), ["numberofemployees"] = 250 };
            context.Initialize(account);
            var order = new Entity("salesorder") { Id = Guid.NewGuid(), ["customerid"] = account.ToEntityReference() };

            context.ExecutePluginWithTarget<ApplyDiscountPlugin>(order);

            Assert.Equal(10m, order["discountpercentage"]);
        }

        [Fact]
        public void AddExecutionMock_replaces_a_typed_request()
        {
            var context = new XrmFakedContext();
            context.AddExecutionMock<RetrieveVersionRequest>(request => new RetrieveVersionResponse
            {
                Results = new ParameterCollection { { "Version", "9.1.0.1234" } }
            });

            var response = (RetrieveVersionResponse)context.GetOrganizationService().Execute(new RetrieveVersionRequest());

            Assert.Equal("9.1.0.1234", response.Version);
        }

        [Fact]
        public void Unsupported_request_throws_PullRequestException()
        {
            var context = new XrmFakedContext();

            Assert.Throws<PullRequestException>(() =>
                context.GetOrganizationService().Execute(new RetrieveAllEntitiesRequest()));
        }
    }
}
