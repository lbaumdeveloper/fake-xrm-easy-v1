using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Query;

namespace FakeXrmEasy
{
    /// <summary>
    /// Base class for plugin and custom workflow activity tests, independent of the test framework.
    /// Records appended to <see cref="Records"/> are loaded into <see cref="Context"/> on <see cref="InitializeContext"/>
    /// or on the first use of <see cref="Service"/> or any helper. Records appended later are loaded on the next such call.
    /// Records without an id get a new one when they are loaded.
    /// </summary>
    /// <remarks>
    /// Test frameworks that reuse one instance for all tests of a class (NUnit, MSTest with shared state)
    /// must call <see cref="ResetContext"/> before each test, for example in a [SetUp] method.
    /// </remarks>
    public abstract class XrmFakedTestBase
    {
        public const string PreImageName = "PreImage";
        public const string PostImageName = "PostImage";
        public const int PreValidationStage = 10;
        public const int PreOperationStage = 20;
        public const int PostOperationStage = 40;

        private XrmFakedContext _context;
        private IOrganizationService _service;
        private int _loadedRecordCount;

        protected XrmFakedTestBase()
        {
            Records = new List<Entity>();
        }

        /// <summary>
        /// The faked context. Call <see cref="InitializeContext"/> before using it directly so pending records are loaded.
        /// </summary>
        protected XrmFakedContext Context => _context ?? (_context = CreateContext());

        /// <summary>
        /// Records to seed the context with. Append only: removing records after they were loaded has no effect.
        /// </summary>
        protected List<Entity> Records { get; private set; }

        /// <summary>
        /// Organization service of the context. Loads pending records first.
        /// </summary>
        protected IOrganizationService Service
        {
            get
            {
                InitializeContext();
                return _service ?? (_service = Context.GetOrganizationService());
            }
        }

        /// <summary>
        /// Tracing service handed to plugins and workflow activities executed through this class.
        /// </summary>
        protected XrmFakedTracingService TracingService => Context.GetFakeTracingService();

        /// <summary>
        /// Everything traced so far.
        /// </summary>
        protected string TraceLog => TracingService.DumpTrace();

        /// <summary>
        /// Creates the context. Override to configure it, for example with proxy types, metadata or a caller.
        /// </summary>
        protected virtual XrmFakedContext CreateContext()
        {
            return new XrmFakedContext();
        }

        /// <summary>
        /// Discards the context and all records so the next test starts empty.
        /// </summary>
        protected void ResetContext()
        {
            _context = null;
            _service = null;
            _loadedRecordCount = 0;
            Records = new List<Entity>();
        }

        /// <summary>
        /// Loads all records not yet in the context. Safe to call any number of times.
        /// </summary>
        protected void InitializeContext()
        {
            var pending = Records.Skip(_loadedRecordCount).ToList();
            foreach (var record in pending.Where(r => r.Id == Guid.Empty))
            {
                record.Id = Guid.NewGuid();
            }

            if (!Context.Initialised)
            {
                Context.Initialize(pending);
            }
            else
            {
                foreach (var record in pending)
                {
                    Context.AddEntityWithDefaults(record, clone: true);
                }
            }

            _loadedRecordCount = Records.Count;
        }

        /// <summary>
        /// Appends records to <see cref="Records"/> and loads everything into the context.
        /// </summary>
        protected void InitializeContext(params Entity[] records)
        {
            Records.AddRange(records);
            InitializeContext();
        }

        /// <summary>
        /// Appends a systemuser record with a new id to <see cref="Records"/>.
        /// </summary>
        protected Entity AddUser(string fullName = "Test User")
        {
            var user = new Entity("systemuser") { Id = Guid.NewGuid(), ["fullname"] = fullName };
            Records.Add(user);
            return user;
        }

        /// <summary>
        /// Executes all following requests, plugins and workflow activities as the given user.
        /// </summary>
        protected void SetCaller(EntityReference user)
        {
            Context.CallerId = user;
        }

        /// <summary>
        /// Executes all following requests, plugins and workflow activities as the given user.
        /// </summary>
        protected void SetCaller(Entity user)
        {
            SetCaller(user.ToEntityReference());
        }

        /// <summary>
        /// Creates a record through the organization service.
        /// </summary>
        protected Guid Create(Entity record)
        {
            return Service.Create(record);
        }

        /// <summary>
        /// Retrieves a record. Without columns all columns are returned.
        /// </summary>
        protected Entity Retrieve(string logicalName, Guid id, params string[] columns)
        {
            return Service.Retrieve(logicalName, id, ToColumnSet(columns));
        }

        /// <summary>
        /// Retrieves a record. Without columns all columns are returned.
        /// </summary>
        protected Entity Retrieve(EntityReference reference, params string[] columns)
        {
            return Retrieve(reference.LogicalName, reference.Id, columns);
        }

        /// <summary>
        /// Retrieves an early-bound record. Without columns all columns are returned.
        /// </summary>
        protected TEntity Retrieve<TEntity>(Guid id, params string[] columns) where TEntity : Entity
        {
            return Retrieve(GetLogicalName<TEntity>(), id, columns).ToEntity<TEntity>();
        }

        /// <summary>
        /// Retrieves the current state of a record from the context with all columns.
        /// </summary>
        protected TEntity Reload<TEntity>(TEntity record) where TEntity : Entity
        {
            return Retrieve(record.ToEntityReference()).ToEntity<TEntity>();
        }

        /// <summary>
        /// Updates a record through the organization service.
        /// </summary>
        protected void Update(Entity record)
        {
            Service.Update(record);
        }

        /// <summary>
        /// Deletes a record through the organization service.
        /// </summary>
        protected void Delete(EntityReference reference)
        {
            Service.Delete(reference.LogicalName, reference.Id);
        }

        /// <summary>
        /// Executes a request through the organization service.
        /// </summary>
        protected TResponse Execute<TResponse>(OrganizationRequest request) where TResponse : OrganizationResponse
        {
            return (TResponse)Service.Execute(request);
        }

        /// <summary>
        /// Runs a query expression, query by attribute or fetch expression.
        /// </summary>
        protected List<Entity> RetrieveMultiple(QueryBase query)
        {
            return Service.RetrieveMultiple(query).Entities.ToList();
        }

        /// <summary>
        /// Runs a FetchXML query.
        /// </summary>
        protected List<Entity> RetrieveMultiple(string fetchXml)
        {
            return RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// LINQ query over the records of one table.
        /// </summary>
        protected IQueryable<Entity> Query(string logicalName)
        {
            InitializeContext();
            return Context.CreateQuery(logicalName);
        }

        /// <summary>
        /// LINQ query over the records of one early-bound table.
        /// </summary>
        protected IQueryable<TEntity> Query<TEntity>() where TEntity : Entity
        {
            InitializeContext();
            return Context.CreateQuery<TEntity>();
        }

        /// <summary>
        /// All records of one table.
        /// </summary>
        protected List<Entity> GetAll(string logicalName)
        {
            return Query(logicalName).ToList();
        }

        /// <summary>
        /// All records of one early-bound table.
        /// </summary>
        protected List<TEntity> GetAll<TEntity>() where TEntity : Entity
        {
            return Query<TEntity>().ToList();
        }

        /// <summary>
        /// The only record of a table matching the predicate. Throws when there is none or more than one.
        /// </summary>
        protected Entity Single(string logicalName, Func<Entity, bool> predicate)
        {
            return GetAll(logicalName).Single(predicate);
        }

        /// <summary>
        /// Number of records in a table.
        /// </summary>
        protected int Count(string logicalName)
        {
            InitializeContext();
            return Context.Data.TryGetValue(logicalName, out var records) ? records.Count : 0;
        }

        /// <summary>
        /// Whether the record exists in the context.
        /// </summary>
        protected bool Exists(EntityReference reference)
        {
            InitializeContext();
            return Context.Data.TryGetValue(reference.LogicalName, out var records) && records.ContainsKey(reference.Id);
        }

        /// <summary>
        /// Builds a plugin execution context for a message, with the target and the optional images under
        /// <see cref="PreImageName"/> and <see cref="PostImageName"/>.
        /// </summary>
        /// <param name="target">An <see cref="Entity"/> or <see cref="EntityReference"/>; null for messages without a target.</param>
        protected XrmFakedPluginExecutionContext CreatePluginContext(string messageName, object target, int stage = PostOperationStage, Entity preImage = null, Entity postImage = null)
        {
            InitializeContext();
            var pluginContext = Context.GetDefaultPluginContext();
            pluginContext.MessageName = messageName;
            pluginContext.Stage = stage;

            if (target != null)
            {
                var reference = target as EntityReference ?? (target as Entity)?.ToEntityReference()
                    ?? throw new ArgumentException("The target must be an Entity or an EntityReference.", nameof(target));

                pluginContext.InputParameters["Target"] = target;
                pluginContext.PrimaryEntityName = reference.LogicalName;
                pluginContext.PrimaryEntityId = reference.Id;
            }

            if (preImage != null)
            {
                pluginContext.PreEntityImages.Add(PreImageName, preImage);
            }

            if (postImage != null)
            {
                pluginContext.PostEntityImages.Add(PostImageName, postImage);
            }

            return pluginContext;
        }

        /// <summary>
        /// Executes a plugin with the given execution context.
        /// </summary>
        protected void ExecutePlugin<TPlugin>(XrmFakedPluginExecutionContext pluginContext) where TPlugin : IPlugin, new()
        {
            ExecutePlugin(new TPlugin(), pluginContext);
        }

        /// <summary>
        /// Executes a plugin instance, for example one built with a secure or unsecure configuration.
        /// </summary>
        protected void ExecutePlugin(IPlugin plugin, XrmFakedPluginExecutionContext pluginContext)
        {
            InitializeContext();
            Context.ExecutePluginWith(pluginContext, plugin);
        }

        /// <summary>
        /// Executes a plugin on Create of the target.
        /// </summary>
        /// <returns>The execution context, to assert output parameters or shared variables.</returns>
        protected XrmFakedPluginExecutionContext ExecutePluginOnCreate<TPlugin>(Entity target, int stage = PostOperationStage) where TPlugin : IPlugin, new()
        {
            var pluginContext = CreatePluginContext("Create", target, stage);
            ExecutePlugin<TPlugin>(pluginContext);
            return pluginContext;
        }

        /// <summary>
        /// Executes a plugin on Update of the target with optional pre and post images.
        /// </summary>
        /// <returns>The execution context, to assert output parameters or shared variables.</returns>
        protected XrmFakedPluginExecutionContext ExecutePluginOnUpdate<TPlugin>(Entity target, Entity preImage = null, Entity postImage = null, int stage = PostOperationStage) where TPlugin : IPlugin, new()
        {
            var pluginContext = CreatePluginContext("Update", target, stage, preImage, postImage);
            ExecutePlugin<TPlugin>(pluginContext);
            return pluginContext;
        }

        /// <summary>
        /// Executes a plugin on Delete of the target with an optional pre image.
        /// </summary>
        /// <returns>The execution context, to assert output parameters or shared variables.</returns>
        protected XrmFakedPluginExecutionContext ExecutePluginOnDelete<TPlugin>(EntityReference target, Entity preImage = null, int stage = PostOperationStage) where TPlugin : IPlugin, new()
        {
            var pluginContext = CreatePluginContext("Delete", target, stage, preImage);
            ExecutePlugin<TPlugin>(pluginContext);
            return pluginContext;
        }

        /// <summary>
        /// Builds a workflow context with the given primary record.
        /// </summary>
        protected XrmFakedWorkflowContext CreateWorkflowContext(EntityReference primaryEntity = null)
        {
            InitializeContext();
            var workflowContext = Context.GetDefaultWorkflowContext();
            if (primaryEntity != null)
            {
                workflowContext.PrimaryEntityName = primaryEntity.LogicalName;
                workflowContext.PrimaryEntityId = primaryEntity.Id;
            }

            return workflowContext;
        }

        /// <summary>
        /// Executes a custom workflow activity for the primary record.
        /// </summary>
        /// <param name="inputs">Input arguments by property name.</param>
        /// <returns>Output arguments by property name.</returns>
        protected IDictionary<string, object> ExecuteWorkflowActivity<TActivity>(EntityReference primaryEntity = null, Dictionary<string, object> inputs = null) where TActivity : CodeActivity, new()
        {
            return ExecuteWorkflowActivity<TActivity>(CreateWorkflowContext(primaryEntity), inputs);
        }

        /// <summary>
        /// Executes a custom workflow activity with the given workflow context.
        /// </summary>
        /// <param name="inputs">Input arguments by property name.</param>
        /// <returns>Output arguments by property name.</returns>
        protected IDictionary<string, object> ExecuteWorkflowActivity<TActivity>(XrmFakedWorkflowContext workflowContext, Dictionary<string, object> inputs = null) where TActivity : CodeActivity, new()
        {
            InitializeContext();
            return Context.ExecuteCodeActivity<TActivity>(workflowContext, inputs ?? new Dictionary<string, object>());
        }

        private static ColumnSet ToColumnSet(string[] columns)
        {
            return columns == null || columns.Length == 0 ? new ColumnSet(true) : new ColumnSet(columns);
        }

        private static string GetLogicalName<TEntity>() where TEntity : Entity
        {
            var attribute = typeof(TEntity).GetCustomAttribute<EntityLogicalNameAttribute>();
            if (attribute == null)
            {
                throw new InvalidOperationException($"{typeof(TEntity).Name} has no EntityLogicalNameAttribute. Use the overload taking a logical name.");
            }

            return attribute.LogicalName;
        }
    }
}
