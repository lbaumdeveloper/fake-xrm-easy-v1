using System;
using Microsoft.Xrm.Sdk;

namespace FakeXrmEasy.Tests.PluginsForTesting
{
    /// <summary>
    /// Creates a contact for every target it receives.
    /// </summary>
    public class CreateContactForTargetPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var service = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(context.UserId);
            service.Create(new Entity("contact") { ["lastname"] = "Nested" });
        }
    }

    /// <summary>
    /// Updates its own target again, which loops forever on the server.
    /// </summary>
    public class UpdateTargetAgainPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var service = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(context.UserId);
            var target = (Entity)context.InputParameters["Target"];
            service.Update(new Entity(target.LogicalName) { Id = target.Id, ["description"] = Guid.NewGuid().ToString() });
        }
    }

    /// <summary>
    /// Always fails with an InvalidPluginExecutionException.
    /// </summary>
    public class FailingPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            throw new InvalidPluginExecutionException("Validation failed");
        }
    }
}
