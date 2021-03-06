namespace MassTransit.RabbitMqTransport.Contexts
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Configuration;
    using GreenPipes;
    using RabbitMQ.Client;
    using Topology;
    using Transports;
    using Util;


    public class RabbitMqConnectionContext :
        BasePipeContext,
        ConnectionContext,
        IAsyncDisposable
    {
        readonly IConnection _connection;
        readonly LimitedConcurrencyLevelTaskScheduler _taskScheduler;

        public RabbitMqConnectionContext(IConnection connection, IRabbitMqHostConfiguration configuration, IRabbitMqHostTopology hostTopology,
            string description, CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            _connection = connection;

            Description = description;
            HostAddress = configuration.HostAddress;

            PublisherConfirmation = configuration.PublisherConfirmation;
            BatchSettings = configuration.BatchSettings;

            Topology = hostTopology;

            StopTimeout = TimeSpan.FromSeconds(30);

            _taskScheduler = new LimitedConcurrencyLevelTaskScheduler(1);

            connection.ConnectionShutdown += OnConnectionShutdown;
        }

        IConnection ConnectionContext.Connection => _connection;
        public string Description { get; }
        public Uri HostAddress { get; }
        public bool PublisherConfirmation { get; }

        public BatchSettings BatchSettings { get; }

        public TimeSpan StopTimeout { get; }

        public IRabbitMqHostTopology Topology { get; }

        public async Task<IModel> CreateModel(CancellationToken cancellationToken)
        {
            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, cancellationToken);

            var model = await Task.Factory.StartNew(() => _connection.CreateModel(), tokenSource.Token, TaskCreationOptions.None, _taskScheduler)
                .ConfigureAwait(false);

            return model;
        }

        async Task<ModelContext> ConnectionContext.CreateModelContext(CancellationToken cancellationToken)
        {
            var model = await CreateModel(cancellationToken).ConfigureAwait(false);

            return new RabbitMqModelContext(this, model, cancellationToken);
        }

        Task IAsyncDisposable.DisposeAsync(CancellationToken cancellationToken)
        {
            _connection.ConnectionShutdown -= OnConnectionShutdown;

            TransportLogMessages.DisconnectHost(Description);

            _connection.Cleanup(200, "Connection Disposed");

            TransportLogMessages.DisconnectedHost(Description);

            return TaskUtil.Completed;
        }

        void OnConnectionShutdown(object connection, ShutdownEventArgs reason)
        {
            _connection.Cleanup(reason.ReplyCode, reason.ReplyText);
        }
    }
}
