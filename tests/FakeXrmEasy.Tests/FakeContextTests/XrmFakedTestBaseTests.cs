using System;
using System.Collections.Generic;
using System.Linq;
using Crm;
using FakeXrmEasy.Tests.UsageGuide;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NUnit.Framework;

namespace FakeXrmEasy.Tests.FakeContextTests
{
    public class XrmFakedTestBaseTests : XrmFakedTestBase
    {
        [SetUp]
        public void SetUp() => ResetContext();

        [Test]
        public void Records_are_loaded_on_first_service_use()
        {
            var account = new Entity("account") { ["name"] = "Contoso Ltd" };
            Records.Add(account);

            var accounts = RetrieveMultiple(new QueryExpression("account") { ColumnSet = new ColumnSet("name") });

            Assert.That(accounts.Single().GetAttributeValue<string>("name"), Is.EqualTo("Contoso Ltd"));
            Assert.That(accounts.Single().Id, Is.EqualTo(account.Id));
            Assert.That(account.Id, Is.Not.EqualTo(Guid.Empty));
        }

        [Test]
        public void Records_added_after_initialization_are_loaded_on_next_use()
        {
            Records.Add(new Entity("account") { ["name"] = "First" });
            InitializeContext();

            Records.Add(new Entity("account") { ["name"] = "Second" });
            InitializeContext(new Entity("account") { ["name"] = "Third" });

            Assert.That(Count("account"), Is.EqualTo(3));
            Assert.That(GetAll("account").Select(a => a.GetAttributeValue<string>("name")), Is.EquivalentTo(new[] { "First", "Second", "Third" }));
        }

        [Test]
        public void InitializeContext_can_be_called_repeatedly()
        {
            Records.Add(new Entity("account"));

            InitializeContext();
            InitializeContext();

            Assert.That(Count("account"), Is.EqualTo(1));
        }

        [Test]
        public void Changes_to_seed_instances_after_loading_do_not_change_the_context()
        {
            var account = new Entity("account") { ["name"] = "Original" };
            Records.Add(account);
            InitializeContext();

            account["name"] = "Changed";

            Assert.That(Reload(account).GetAttributeValue<string>("name"), Is.EqualTo("Original"));
        }

        [Test]
        public void ResetContext_discards_records_and_context()
        {
            Records.Add(new Entity("account"));
            InitializeContext();
            var previous = Context;

            ResetContext();

            Assert.That(Context, Is.Not.SameAs(previous));
            Assert.That(Records, Is.Empty);
            Assert.That(Count("account"), Is.Zero);
        }

        [Test]
        public void Early_bound_records_can_be_retrieved_and_queried()
        {
            var account = new Account { Id = Guid.NewGuid(), Name = "Contoso Ltd" };
            Records.Add(account);

            Assert.That(Retrieve<Account>(account.Id).Name, Is.EqualTo("Contoso Ltd"));
            Assert.That(GetAll<Account>(), Has.Count.EqualTo(1));
            Assert.That(Query<Account>().Single(a => a.Name == "Contoso Ltd").Id, Is.EqualTo(account.Id));
        }

        [Test]
        public void Exists_reflects_deletes()
        {
            var account = new Entity("account") { Id = Guid.NewGuid() };
            Records.Add(account);
            Assert.That(Exists(account.ToEntityReference()), Is.True);

            Delete(account.ToEntityReference());

            Assert.That(Exists(account.ToEntityReference()), Is.False);
        }

        [Test]
        public void RetrieveMultiple_runs_query_expressions_and_fetchxml()
        {
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
        }

        [Test]
        public void ExecutePluginOnCreate_runs_the_plugin_against_the_seeded_context()
        {
            var account = new Entity("account") { ["name"] = "Contoso Ltd" };
            Records.Add(account);

            ExecutePluginOnCreate<AccountFollowUpPlugin>(account);

            var task = Single("task", t => t.GetAttributeValue<EntityReference>("regardingobjectid")?.Id == account.Id);
            Assert.That(task.GetAttributeValue<string>("subject"), Is.EqualTo("Welcome call for Contoso Ltd"));
        }

        [Test]
        public void ExecutePluginOnUpdate_passes_the_pre_image_and_traces()
        {
            var contact = new Entity("contact") { Id = Guid.NewGuid(), ["lastname"] = "Baum" };
            var preImage = new Entity("contact") { Id = contact.Id, ["firstname"] = "Lukas", ["lastname"] = "Old" };

            var pluginContext = ExecutePluginOnUpdate<ContactNormalizePlugin>(contact, preImage, stage: PreOperationStage);

            Assert.That(contact.GetAttributeValue<string>("fullname"), Is.EqualTo("Lukas Baum"));
            Assert.That(pluginContext.PrimaryEntityId, Is.EqualTo(contact.Id));
            Assert.That(TraceLog, Does.Contain("Full name set to Lukas Baum"));
        }

        [Test]
        public void ExecutePluginOnDelete_surfaces_plugin_exceptions()
        {
            var account = new Entity("account") { Id = Guid.NewGuid() };
            Records.Add(account);
            Records.Add(new Entity("opportunity") { ["parentaccountid"] = account.ToEntityReference(), ["statecode"] = new OptionSetValue(0) });

            Action delete = () => ExecutePluginOnDelete<PreventAccountDeletePlugin>(account.ToEntityReference(), stage: PreValidationStage);

            Assert.That(delete, Throws.TypeOf<InvalidPluginExecutionException>());
        }

        [Test]
        public void ExecutePlugin_accepts_configured_instances()
        {
            var account = new Entity("account") { Id = Guid.NewGuid() };

            ExecutePlugin(new DefaultCreditLimitPlugin("1500", null), CreatePluginContext("Create", account, PreOperationStage));

            Assert.That(account.GetAttributeValue<Money>("creditlimit").Value, Is.EqualTo(1500m));
        }

        [Test]
        public void ExecuteWorkflowActivity_returns_outputs_and_runs_as_caller()
        {
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
        }
    }

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
}
