using System;
using Microsoft.Xrm.Sdk;

namespace FakeXrmEasy.FakeMessageExecutors
{
    /// <summary>
    /// Executes a custom action registered through <see cref="XrmFakedContext.RegisterCustomAction(string, Func{IOrganizationService, OrganizationRequest, ParameterCollection})"/>.
    /// Not discovered automatically because it has no parameterless constructor.
    /// </summary>
    public class CustomActionExecutor : IFakeMessageExecutor
    {
        private readonly string _actionName;
        private readonly Func<IOrganizationService, OrganizationRequest, ParameterCollection> _handler;

        public CustomActionExecutor(string actionName, Func<IOrganizationService, OrganizationRequest, ParameterCollection> handler)
        {
            _actionName = actionName;
            _handler = handler;
        }

        public bool CanExecute(OrganizationRequest request)
        {
            return string.Equals(request.RequestName, _actionName, StringComparison.Ordinal);
        }

        public OrganizationResponse Execute(OrganizationRequest request, XrmFakedContext ctx)
        {
            return new OrganizationResponse
            {
                ResponseName = _actionName,
                Results = _handler(ctx.GetOrganizationService(), request) ?? new ParameterCollection()
            };
        }

        public Type GetResponsibleRequestType()
        {
            return typeof(OrganizationRequest);
        }
    }
}
