using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public CommandSagaHandler(IMessageSerializer serializer, IVersionRegistrar registrar)
        {
            _serializer = serializer;
            _registrar = registrar;
        }

        protected override void ConfigureHowToFindSaga(SagaPropertyMapper<CommandSagaHandler.SagaData> mapper)
        {
            mapper.ConfigureMapping<StartCommandSaga>(x => x.SagaId).ToSaga(x => x.SagaId);
            mapper.ConfigureMapping<ContinueCommandSaga>(x => x.SagaId).ToSaga(x => x.SagaId);
            mapper.ConfigureMapping<AbortCommandSaga>(x => x.SagaId).ToSaga(x => x.SagaId);
        }
        public async Task Handle(StartCommandSaga message, IMessageHandlerContext context)
        {
            var originating = new MessageData
            {
                Version = _registrar.GetVersionedName(message.Originating.GetType()),
                Message = _serializer.Serialize(message.Originating).AsString()
            };

            Data.CurrentIndex = 0;
            Data.Originating = originating;
            Data.Commands = message.Commands.Select(x => new MessageData
            {
                Version = _registrar.GetVersionedName(x.GetType()),
                Message = _serializer.Serialize(x).AsString()
            }).ToArray();
            Data.AbortCommands = message.AbortCommands.Select(x => new MessageData
            {
                Version = _registrar.GetVersionedName(x.GetType()),
                Message = _serializer.Serialize(x).AsString()
            }).ToArray();

            await RequestTimeout(context, TimeSpan.FromMinutes(10), new TimeoutMessage { SagaId = Data.SagaId });
            // Send first command
            await SendNextCommand(context);
        }
        public Task Handle(ContinueCommandSaga message, IMessageHandlerContext context)
        {
            Data.CurrentIndex++;

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

                var message = _serializer.Deserialize(
                    _registrar.GetNamedType(data.Version),
                    data.Message.AsByteArray()
                    ) as Messages.ICommand;

                await context.Send(message, options).ConfigureAwait(false);
            }
            catch
            {
                await Handle(new AbortCommandSaga { SagaId = Data.SagaId }, context);
            }


        }
    }
}
