using System;
using System.Linq;
using Crm;
using FakeXrmEasy.Tests.PluginsForTesting;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace FakeXrmEasy.Tests.Pipeline
{
    public class PipelineImageTests
    {
        private readonly XrmFakedContext _context;
        private readonly IOrganizationService _service;
        private readonly Guid _accountId;

        public PipelineImageTests()
        {
            ContextCapturePlugin.Contexts.Clear();
            _context = new XrmFakedContext { UsePipelineSimulation = true };
            _service = _context.GetOrganizationService();
            _accountId = Guid.NewGuid();
            _context.Initialize(new Account { Id = _accountId, Name = "Old name", AccountNumber = "A-1" });
        }

        [Fact]
        public void Update_step_receives_pre_and_post_images_with_old_and_new_values()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Update", ProcessingStepStage.Postoperation,
                images: new[] { new PluginStepImage("image", ProcessingStepImageType.Both) });

            _service.Update(new Account { Id = _accountId, Name = "New name" });

            var pluginContext = ContextCapturePlugin.Contexts.Single();
            Assert.Equal("Old name", pluginContext.PreEntityImages["image"].GetAttributeValue<string>("name"));
            Assert.Equal("New name", pluginContext.PostEntityImages["image"].GetAttributeValue<string>("name"));
            Assert.Equal("A-1", pluginContext.PostEntityImages["image"].GetAttributeValue<string>("accountnumber"));
        }

        [Fact]
        public void Image_contains_only_registered_attributes()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Update", ProcessingStepStage.Preoperation,
                images: new[] { new PluginStepImage("pre", ProcessingStepImageType.PreImage, "name") });

            _service.Update(new Account { Id = _accountId, Name = "New name" });

            var preImage = ContextCapturePlugin.Contexts.Single().PreEntityImages["pre"];
            Assert.Equal("Old name", preImage.GetAttributeValue<string>("name"));
            Assert.False(preImage.Contains("accountnumber"));
            Assert.Equal(_accountId, preImage.Id);
        }

        [Fact]
        public void Pre_operation_step_receives_no_post_image()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Update", ProcessingStepStage.Preoperation,
                images: new[] { new PluginStepImage("image", ProcessingStepImageType.Both) });

            _service.Update(new Account { Id = _accountId, Name = "New name" });

            var pluginContext = ContextCapturePlugin.Contexts.Single();
            Assert.True(pluginContext.PreEntityImages.ContainsKey("image"));
            Assert.False(pluginContext.PostEntityImages.ContainsKey("image"));
        }

        [Fact]
        public void Create_post_operation_step_receives_post_image()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Create", ProcessingStepStage.Postoperation,
                images: new[] { new PluginStepImage("post", ProcessingStepImageType.PostImage) });

            var id = _service.Create(new Account { Name = "Created" });

            var postImage = ContextCapturePlugin.Contexts.Single().PostEntityImages["post"];
            Assert.Equal(id, postImage.Id);
            Assert.Equal("Created", postImage.GetAttributeValue<string>("name"));
        }

        [Fact]
        public void Delete_step_receives_pre_image_of_deleted_record()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Delete", ProcessingStepStage.Postoperation,
                images: new[] { new PluginStepImage("pre", ProcessingStepImageType.PreImage) });

            _service.Delete(Account.EntityLogicalName, _accountId);

            var preImage = ContextCapturePlugin.Contexts.Single().PreEntityImages["pre"];
            Assert.Equal("Old name", preImage.GetAttributeValue<string>("name"));
        }

        [Fact]
        public void Step_without_images_receives_empty_image_collections()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Update");

            _service.Update(new Account { Id = _accountId, Name = "New name" });

            var pluginContext = ContextCapturePlugin.Contexts.Single();
            Assert.Empty(pluginContext.PreEntityImages);
            Assert.Empty(pluginContext.PostEntityImages);
        }

        [Fact]
        public void Update_step_with_filtering_attributes_runs_when_a_filtered_attribute_changes()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Update", filteringAttributes: new[] { "name" });

            _service.Update(new Account { Id = _accountId, Name = "New name" });

            Assert.Single(ContextCapturePlugin.Contexts);
        }

        [Fact]
        public void Update_step_with_filtering_attributes_is_skipped_when_no_filtered_attribute_changes()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Update", filteringAttributes: new[] { "name" });

            _service.Update(new Account { Id = _accountId, AccountNumber = "A-2" });

            Assert.Empty(ContextCapturePlugin.Contexts);
        }

        [Fact]
        public void Registering_two_steps_for_the_same_plugin_reuses_the_plugin_type()
        {
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Create");
            _context.RegisterPluginStep<ContextCapturePlugin, Account>("Update");

            Assert.Single(_context.CreateQuery("plugintype").ToList());
        }
    }
}
