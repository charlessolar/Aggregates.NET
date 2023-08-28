using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Testing.TestableContext.Fakes {
    public class FakeState : Aggregates.State<FakeState> {
        public bool LoadedSnap { get; set; } = false;
    }
    public class FakeEntity : Aggregates.Entity<FakeEntity, FakeState> {
        private FakeEntity() { }

        public void RaiseEvent(string content = "") {
            Apply<FakeEvent>(e => {
                e.Content = content;
            });
        }
        public void RaiseOtherEvent(string content = "") {
            Apply<FakeEvent2>(e => {
                e.Content = content;
            });
        }
    }
    public class FakeChildState : Aggregates.State<FakeChildState, FakeState> {
        public bool LoadedSnap { get; set; } = false;
    }
    public class FakeChildEntity : Aggregates.Entity<FakeChildEntity, FakeChildState, FakeEntity> {

        private FakeChildEntity() { }

        public void RaiseEvent(string content = "") {
            Apply<FakeEvent>(e => {
                e.Content = content;
            });
        }
    }
}
