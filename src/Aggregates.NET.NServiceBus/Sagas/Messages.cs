using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Sagas
{
    [Versioned("StartCommandSaga", "Aggregates")]
    public class StartCommandSaga : Messages.IMessage
    {
        public string SagaId { get; set; }
        public Messages.IMessage Originating { get; set; }
        public Messages.ICommand[] Commands { get; set; }
        public Messages.ICommand[] AbortCommands { get; set; }
    }
    [Versioned("StartCommandSaga", "Aggregates")]
    public class ContinueCommandSaga : Messages.IMessage
    {
        public string SagaId { get; set; }
    }
    [Versioned("StartCommandSaga", "Aggregates")]
    public class AbortCommandSaga : Messages.IMessage
    {
        public string SagaId { get; set; }
    }
}
