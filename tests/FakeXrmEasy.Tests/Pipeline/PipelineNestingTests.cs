using System;
using System.Linq;
using System.ServiceModel;
using Crm;
using FakeXrmEasy.Tests.PluginsForTesting;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace FakeXrmEasy.Tests.Pipeline
{
    [Collection(nameof(ContextCapturePlugin))]
    public class PipelineNestingTests
    {
        private readonly XrmFakedContext _context;
        private readonly IOrganizationService _service;

        public PipelineNestingTests()
        {
            ContextCapturePlugin.Contexts.Clear();
            _context = new XrmFakedContext { UsePipelineSimulation = true };
            _service = _context.GetOrganizationService();
        }

        [Fact]
        public void Prevalidation_step_runs_before_preoperation_step()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Create", ProcessingStepStage.Preoperation);
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Create", ProcessingStepStage.Prevalidation);

            _service.Create(new Account());

            Assert.Equal(new[] { 10, 20 }, ContextCapturePlugin.Contexts.Select(c => c.Stage).ToArray());
        }

        [Fact]
        public void Step_triggered_by_another_step_gets_increased_depth_and_parent_context()
        {
            _context.RegisterPluginStep<CreateContactForTargetPlugin, Account>("Create");
            _context.RegisterPluginStep<ContextCapturePlugin, Contact>("Create");

            _service.Create(new Account());

            var nested = ContextCapturePlugin.Contexts.Single();
            Assert.Equal(2, nested.Depth);
            Assert.Equal("Create", nested.ParentContext.MessageName);
            Assert.Equal(Account.EntityLogicalName, ((Entity)nested.ParentContext.InputParameters["Target"]).LogicalName);
        }

        [Fact]
        public void Step_triggered_by_a_directly_executed_plugin_gets_depth_two()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Contact>("Create");

            _context.ExecutePluginWithTarget<CreateContactForTargetPlugin>(new Account { Id = Guid.NewGuid() });

            Assert.Equal(2, ContextCapturePlugin.Contexts.Single().Depth);
        }

        [Fact]
        public void Top_level_step_has_depth_one()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Create");

            _service.Create(new Account());

            var context = ContextCapturePlugin.Contexts.Single();
            Assert.Equal(1, context.Depth);
            Assert.Null(context.ParentContext);
        }

        [Fact]
        public void Infinite_loop_throws_when_max_depth_is_exceeded()
        {
            var id = Guid.NewGuid();
            _context.Initialize(new Account { Id = id });
            _context.RegisterPluginStep<UpdateTargetAgainPlugin, Account>("Update");

            Assert.Throws<FaultException<OrganizationServiceFault>>(() => _service.Update(new Account { Id = id, Name = "start" }));
        }

        [Fact]
        public void Max_depth_is_configurable()
        {
            var id = Guid.NewGuid();
            _context.Initialize(new Account { Id = id });
            _context.MaxPipelineDepth = 2;
            _context.RegisterPluginStep<UpdateTargetAgainPlugin, Account>("Update");
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Update", rank: 2);

            Assert.Throws<FaultException<OrganizationServiceFault>>(() => _service.Update(new Account { Id = id, Name = "start" }));
            Assert.Empty(ContextCapturePlugin.Contexts);
        }

        [Fact]
        public void Exception_from_a_step_reaches_the_caller_unwrapped()
        {
            _context.RegisterPluginStep<FailingPlugin, Account>("Create", ProcessingStepStage.Prevalidation);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => _service.Create(new Account()));
            Assert.Equal("Validation failed", ex.Message);
        }
    }
}
