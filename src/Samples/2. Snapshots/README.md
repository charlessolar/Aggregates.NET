# Snapshots

The purpose of this sample is to illustrate the taking of state snapshots at certain intervals.  In this example snapshots are taken every 5 events using the logic

```
protected override bool ShouldSnapshot()
{
	// Take snapshot every 5 events
    return (Version - (Snapshot?.Version ?? 0)) > 5;
}
```

This is checked on the state object.  There are also two overridable methods

```
protected override void SnapshotRestored()
{
	
}
protected override void Snapshotting()
{
	
}
```

on both the entity and state object.  

`Snapshotting` is called when a snapshot is being taken - it will give you the opportunity to modify the state object before its saved.

`SnapshotRestored` is called when a snapshot is restored - it can be used to do post-processing on a state after loading from a snapshot.
