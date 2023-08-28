using Aggregates.Messages;

namespace Aggregates.Testing.TestableContext.Fakes {
    public class FakeEvent : IEvent {
        public string EntityId { get; set; }
        public bool CreateModel { get; set; }
        public bool DeleteModel { get; set; }
        public bool UpdateModel { get; set; }
        public bool ReadModel { get; set; }
        public string Content { get; set; }
    }
    public class FakeEvent2 : IEvent {
        public string Content { get; set; }
    }
}
