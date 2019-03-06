using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Sagas
{
    public class StartCommandSaga : Messages.IMessage
    {
        public string SagaId { get; set; }
        public Messages.IMessage Originating { get; set; }
        public Messages.ICommand[] Commands { get; set; }
        public Messages.ICommand[] AbortCommands { get; set; }
    }
    public class ContinueCommandSaga : Messages.IMessage
    {
        public string SagaId { get; set; }
    }
    public class AbortCommandSaga : Messages.IMessage
    {
        public string SagaId { get; set; }
    }
}
