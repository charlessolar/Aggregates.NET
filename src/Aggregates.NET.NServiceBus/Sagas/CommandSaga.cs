using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Sagas
{
    
    public class CommandSaga
    {
        private IMessageHandlerContext _context;
        private IVersionRegistrar _versionRegistrar;
        private IMessageSerializer _serializer;
        private string _sagaId;
        private CommandSagaHandler.MessageData _originating;
        private List<CommandSagaHandler.MessageData> _commands;
        private List<CommandSagaHandler.MessageData> _abortCommands;
        private string _domainDestination;

        internal CommandSaga(IMessageHandlerContext context, string sagaId, Messages.IMessage originating, string domainDestimation)
        {
            // Getting the provider here is icky
            // but the problem is this start message needs to describe the messages inside otherwise the types
            // dont survive persistance

            _context = context;
            var provider = context.Extensions.Get<IServiceProvider>();
            _versionRegistrar = provider.GetRequiredService<IVersionRegistrar>();
            _serializer = provider.GetRequiredService<IMessageSerializer>();
            _sagaId = sagaId;
            _originating = new CommandSagaHandler.MessageData
            {
                Version = _versionRegistrar.GetVersionedName(originating.GetType()),
                Message = _serializer.Serialize(originating).AsString()
            };
            _domainDestination = domainDestimation;
            _commands = new List<CommandSagaHandler.MessageData>();
            _abortCommands = new List<CommandSagaHandler.MessageData>();

            if (string.IsNullOrEmpty(_domainDestination))
                throw new ArgumentException($"Usage of SAGA needs a domain destination or specify Configuration.SetCommandDestination");
        }

        public CommandSaga Command(Messages.ICommand command)
        {
            _commands.Add(new CommandSagaHandler.MessageData
            {
                Version = _versionRegistrar.GetVersionedName(command.GetType()),
                Message = _serializer.Serialize(command).AsString()
            });
            return this;
        }

        public CommandSaga OnAbort(Messages.ICommand command)
        {
            _abortCommands.Add(new CommandSagaHandler.MessageData
            {
                Version = _versionRegistrar.GetVersionedName(command.GetType()),
                Message = _serializer.Serialize(command).AsString()
            });
            return this;
        }

        public Task Start()
        {
            var message = new StartCommandSaga
            {
                SagaId = _sagaId,
                Originating = _originating,
                Commands = _commands.ToArray(),
                AbortCommands = _abortCommands.ToArray(),
            };


            var options = new SendOptions();
            options.SetDestination(_domainDestination);
            options.SetHeader(Defaults.RequestResponse, "0");
            options.SetHeader(Defaults.SagaHeader, message.SagaId);

            return _context.Send(message, options);
        }

    }
    
}
