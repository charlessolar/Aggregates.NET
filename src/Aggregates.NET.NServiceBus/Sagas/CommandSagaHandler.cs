using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus;
using System;
using System.Threading.Tasks;

namespace Aggregates.Sagas
{
    public class CommandSagaHandler :
        Saga<CommandSagaHandler.SagaData>,
        IAmStartedByMessages<StartCommandSaga>,
        IHandleMessages<ContinueCommandSaga>,
        IHandleMessages<AbortCommandSaga>,
        IHandleTimeouts<CommandSagaHandler.TimeoutMessage>
    {
        private readonly ILogger _logger;
        private readonly IMessageSerializer _serializer;
        private readonly IVersionRegistrar _registrar;

        public class SagaData : ContainSagaData
        {
            public string SagaId { get; set; }
            public int CurrentIndex { get; set; }
            public bool Aborting { get; set; }
            public MessageData Originating { get; set; }
            public MessageData[] Commands { get; set; }
            public MessageData[] AbortCommands { get; set; }
        }
        public class MessageData
        {
            public string Version { get; set; }
            public string Message { get; set; }
        }
        [Versioned("TimeoutMessage", "Aggregates")]
        public class TimeoutMessage : Messages.IMessage
        {
            public string SagaId { get; set; }
        }

        public CommandSagaHandler(ILogger<CommandSagaHandler> logger, IMessageSerializer serializer, IVersionRegistrar registrar)
        {
            _logger = logger;
            _serializer = serializer;
            _registrar = registrar;
        }

        protected override void ConfigureHowToFindSaga(SagaPropertyMapper<CommandSagaHandler.SagaData> mapper)
        {
            mapper.MapSaga(saga => saga.SagaId)
                .ToMessage<StartCommandSaga>(x => x.SagaId)
                .ToMessage<ContinueCommandSaga>(x => x.SagaId)
                .ToMessage<AbortCommandSaga>(x => x.SagaId);
        }
        public async Task Handle(StartCommandSaga message, IMessageHandlerContext context)
        {

            Data.CurrentIndex = 0;
            Data.Originating = message.Originating;
            Data.Commands = message.Commands;
            Data.AbortCommands = message.AbortCommands;

            _logger.InfoEvent("Saga", "Starting saga {SagaId} originating {OriginatingType} {OriginatingMessage:j}", Data.SagaId, message.Originating.Version, message.Originating.Message);
            await RequestTimeout(context, TimeSpan.FromMinutes(10), new TimeoutMessage { SagaId = Data.SagaId });
            // Send first command
            await SendNextCommand(context);
        }
        public Task Handle(ContinueCommandSaga message, IMessageHandlerContext context)
        {
            Data.CurrentIndex++;
            _logger.DebugEvent("Saga", "Continuing saga {SagaId} {CurrentIndex}/{TotalCommands}", Data.SagaId, Data.CurrentIndex, Data.Commands.Length);

            if (!Data.Aborting && Data.CurrentIndex == Data.Commands.Length)
            {
                MarkAsComplete();
                return Task.CompletedTask;
            }
            if (Data.Aborting && Data.CurrentIndex == Data.AbortCommands.Length)
            {
                MarkAsComplete();
                return Task.CompletedTask;
            }

            // Send next command
            return SendNextCommand(context);
        }
        public Task Handle(AbortCommandSaga message, IMessageHandlerContext context)
        {
            _logger.WarnEvent("Saga", "Aborting saga {SagaId}");
            // some command was rejected - abort
            Data.CurrentIndex = 0;
            Data.Aborting = true;
            return SendNextCommand(context);
        }
        public Task Timeout(TimeoutMessage state, IMessageHandlerContext context)
        {
            // Can receive timeouts when saga is actually complete
            if (!Data.Aborting && Data.CurrentIndex == Data.Commands.Length)
            {
                MarkAsComplete();
                return Task.CompletedTask;
            }
            if (Data.Aborting && Data.CurrentIndex == Data.AbortCommands.Length)
            {
                MarkAsComplete();
                return Task.CompletedTask;
            }

			_logger.WarnEvent("Saga", "Saga {SagaId} has timed out on {CurrentIndex}/{TotalCommands}", Data.SagaId, Data.CurrentIndex, Data.Commands.Length);
			if (!Data.Aborting)
                return Handle(new AbortCommandSaga { SagaId = Data.SagaId }, context);
            try
            {
                var originalMessage = _serializer.Deserialize(
                    _registrar.GetNamedType(Data.Originating.Version),
                    Data.Originating.Message.AsByteArray()
                    ) as Messages.IMessage;
                // a timeout while aborting........
                throw new SagaAbortionFailureException(originalMessage);
            }
            catch
            {
                throw new SagaAbortionFailureException(null);
            }
        }
        private async Task SendNextCommand(IMessageHandlerContext context)
        {
            var destination = context.GetSettings()?.CommandDestination;
            if (string.IsNullOrEmpty(destination))
                throw new Exception("can't handle a saga without a command destination");

            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "1");
            options.SetHeader(Defaults.SagaHeader, Data.SagaId);

            try
            {
                MessageData data;
                if (Data.Aborting)
                    data = Data.AbortCommands[Data.CurrentIndex];
                else
                    data = Data.Commands[Data.CurrentIndex];

                _logger.DebugEvent("Saga", "Saga {SagaId} sending {MessageType} {MessageData:j}", Data.SagaId, data.Version, data.Message);
                var message = _serializer.Deserialize(
                    _registrar.GetNamedType(data.Version),
                    data.Message.AsByteArray()
                    );

                await context.Send(message, options).ConfigureAwait(false);
            }
            catch
            {
                await Handle(new AbortCommandSaga { SagaId = Data.SagaId }, context);
            }


        }
    }
}
