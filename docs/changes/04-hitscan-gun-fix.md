# Session — HitscanGun MissingReferenceException on Stop

**Intent.** User reported error on stopping play after a fire burst:
`MissingReferenceException: LineRenderer has been destroyed`.

**Fix in [HitscanGun.cs](../../Assets/_Project/Scripts/Combat/HitscanGun.cs).**
Two changes:

1. `SpawnTracer` now drains destroyed entries on pop. Loops popping
   until it finds a live `LineRenderer` (Unity's overloaded `==` returns
   true for destroyed objects) before falling through to fresh
   construction.
2. Added `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`
   `ResetStatics` that clears `s_tracerPool`, `s_activeTracers`,
   `s_tracerMaterial` at every Play session start.

**Root cause.** Statics survive domain reload (or "Enter Play Mode
without reload" in modern Unity); the GameObjects they referenced do
not. The pool from session N held references that became fake-null in
session N+1. Same pattern will bite any other static pool we add —
template:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetStatics() { s_pool.Clear(); s_active.Clear(); s_material = null; }
```
