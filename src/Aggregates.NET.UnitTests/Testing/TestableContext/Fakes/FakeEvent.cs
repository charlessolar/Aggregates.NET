using Aggregates.Messages;

namespace Aggregates.Testing.TestableContext.Fakes {
    public class FakeEvent : IEvent {
        public string Content { get; set; }
    }
    public class FakeEvent2 : IEvent {
        public string Content { get; set; }
    }
}
