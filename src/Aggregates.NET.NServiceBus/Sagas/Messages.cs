namespace Aggregates.Sagas
{
    [Versioned("StartCommandSaga", "Aggregates")]
    public class StartCommandSaga : Messages.IMessage
    {
        public string SagaId { get; set; }
        public CommandSagaHandler.MessageData Originating { get; set; }
        public CommandSagaHandler.MessageData[] Commands { get; set; }
        public CommandSagaHandler.MessageData[] AbortCommands { get; set; }
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
