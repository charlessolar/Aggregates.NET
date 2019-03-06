using Aggregates.Extensions;
using NServiceBus;
using System;
using System.Collections.Generic;
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
        public class SagaData : ContainSagaData
        {
            public string SagaId { get; set; }
            public int CurrentIndex { get; set; }
            public bool Aborting { get; set; }
            public Aggregates.Messages.IMessage Originating { get; set; }
            public Aggregates.Messages.ICommand[] Commands { get; set; }
            public Aggregates.Messages.ICommand[] AbortCommands { get; set; }
        }
        public class TimeoutMessage : Messages.IMessage
        {
            public string SagaId { get; set; }
        }

        protected override void ConfigureHowToFindSaga(SagaPropertyMapper<CommandSagaHandler.SagaData> mapper)
        {
            mapper.ConfigureMapping<StartCommandSaga>(x => x.SagaId).ToSaga(x => x.SagaId);
            mapper.ConfigureMapping<ContinueCommandSaga>(x => x.SagaId).ToSaga(x => x.SagaId);
            mapper.ConfigureMapping<AbortCommandSaga>(x => x.SagaId).ToSaga(x => x.SagaId);
        }
        public Task Handle(StartCommandSaga message, IMessageHandlerContext context)
        {
            Data.CurrentIndex = 0;
            Data.Originating = message.Originating;
            Data.Commands = message.Commands;
            Data.AbortCommands = message.AbortCommands;

            // Send first command
            return SendNextCommand(context);
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

            // a timeout while aborting........
            throw new SagaAbortionFailureException(Data.Originating);
        }
        private async Task SendNextCommand(IMessageHandlerContext context)
        {
            var options = new SendOptions();
            options.SetDestination(Configuration.Settings.CommandDestination);
            options.SetHeader(Defaults.RequestResponse, "1");
            options.SetHeader(Defaults.SagaHeader, Data.SagaId);

            Messages.ICommand command;
            if (Data.Aborting)
                command = Data.AbortCommands[Data.CurrentIndex];
            else
                command = Data.Commands[Data.CurrentIndex];

            await context.Send(command, options).ConfigureAwait(false);
            await RequestTimeout(context, TimeSpan.FromSeconds(10), new TimeoutMessage { SagaId = Data.SagaId });
        }
    }
}
