using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace FakeXrmEasy.Tests.PluginsForTesting
{
    /// <summary>
    /// Records every execution context it receives so tests can inspect images and parameters.
    /// </summary>
    public class ContextCapturePlugin : IPlugin
    {
        public static readonly List<IPluginExecutionContext> Contexts = new List<IPluginExecutionContext>();

        public void Execute(IServiceProvider serviceProvider)
        {
            Contexts.Add((IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext)));
        }
    }
}
