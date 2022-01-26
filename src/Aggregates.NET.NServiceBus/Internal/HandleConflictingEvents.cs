using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using NServiceBus;

namespace Aggregates.Internal
{
    /// <summary>
    /// When the above pushes a conflicting event package onto the delayed queue it can end up 
    /// flushed out to the store.  In that case the conflict will be handled via IHandleMessages
    /// The methods here are mostly a hack to rebuild the entity and its parents based on 
    /// information from the conflicting events package.
    /// </summary>
    //internal class HandleConflictingEvents :
    //    IHandleMessages<ConflictingEvents>
    //{
    //    internal static readonly ILog Logger = LogProvider.GetLogger("HandleConflictingEvents");
    //    private readonly IUnitOfWork _uow;

    //    public HandleConflictingEvents(IUnitOfWork uow)
    //    {
    //        _uow = uow;
    //    }

    //    private async Task<IEntity> GetBase(string bucket, string type, Id id, IEntity parent = null)
    //    {
    //        var entityType = Type.GetType(type, false, true);
    //        if (entityType == null)
    //        {
    //            Logger.Error($"Received conflicting events message for unknown type {type}");
    //            throw new ArgumentException($"Received conflicting events message for unknown type {type}");
    //        }

    //        // We have the type name, hack the generic parameters to build Entity
    //        if (parent == null)
    //        {
    //            var method = typeof(IUnitOfWork).GetMethod("For", new Type[] { }).MakeGenericMethod(entityType);

    //            var repo = method.Invoke(_uow, new object[] { });

    //            method = repo.GetType().GetMethod("Get", new Type[] {typeof(string), typeof(Id)});
    //            // Note: we can't just cast method.Invoke into Task<IEventSourced> because Task is not covariant
    //            // believe it or not this is the easiest solution
    //            var task = (Task) method.Invoke(repo, new object[] {bucket, id});
    //            await task.ConfigureAwait(false);
    //            return task.GetType().GetProperty("Result").GetValue(task) as IEntity;
    //        }
    //        else
    //        {
    //            var method =
    //                typeof(IUnitOfWork).GetMethods()
    //                    .Single(x => x.Name == "For" && x.GetParameters().Length == 1)
    //                    .MakeGenericMethod(parent.GetType(), entityType);

    //            var repo = method.Invoke(_uow, new object[] {parent});

    //            method = repo.GetType().GetMethod("Get", new Type[] {typeof(Id)});
    //            var task = (Task) method.Invoke(repo, new object[] {id});
    //            await task.ConfigureAwait(false);
    //            return task.GetType().GetProperty("Result").GetValue(task) as IEntity;
    //        }
    //    }

    //    public async Task Handle(ConflictingEvents conflicts, IMessageHandlerContext ctx)
    //    {
    //        // Hydrate the entity, include all his parents
    //        IEntity parentBase = null;
    //        foreach (var parent in conflicts.Parents)
    //            parentBase = await GetBase(conflicts.Bucket, parent.Item1, parent.Item2, parentBase)
    //                .ConfigureAwait(false);

    //        var target = await GetBase(conflicts.Bucket, conflicts.EntityType, conflicts.StreamId, parentBase)
    //            .ConfigureAwait(false);
    //        var stream = target.Stream;

    //        Logger.Write(LogLevel.Info,
    //            () =>
    //                $"Weakly resolving {conflicts.Events.Count()} conflicts on stream [{conflicts.StreamId}] type [{target.GetType().FullName}] bucket [{target.Stream.Bucket}]");

    //        // No need to pull from the delayed channel or hydrate as below because this is called from the Delayed system which means
    //        // the conflict is not in delayed cache and GetBase above pulls the latest stream

    //        Logger.Write(LogLevel.Debug, () => $"Merging {conflicts.Events.Count()} conflicted events");
    //        try
    //        {
    //            foreach (var u in conflicts.Events)
    //                target.Conflict(u.Event as IEvent,
    //                    metadata:
    //                    new Dictionary<string, string>
    //                    {
    //                        {"ConflictResolution", ConcurrencyConflict.ResolveWeakly.ToString()}
    //                    });
    //        }
    //        catch (NoRouteException e)
    //        {
    //            Logger.Write(LogLevel.Info, () => $"Failed to resolve conflict: {e.Message}");
    //            throw new ConflictResolutionFailedException("Failed to resolve conflict", e);
    //        }

    //        Logger.Write(LogLevel.Info, () => "Successfully merged conflicted events");

    //        if (stream.StreamVersion != stream.CommitVersion && target is ISnapshotting &&
    //            ((ISnapshotting) target).ShouldTakeSnapshot())
    //        {
    //            Logger.Write(LogLevel.Debug,
    //                () =>
    //                    $"Taking snapshot of [{target.GetType().FullName}] id [{target.Id}] version {stream.StreamVersion}");
    //            var memento = ((ISnapshotting) target).TakeSnapshot();
    //            stream.AddSnapshot(memento);
    //        }
    //        // Dont call stream.Commit we are inside a UOW in this method it will do the commit for us

    //    }
    //}
}