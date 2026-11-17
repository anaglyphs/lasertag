# Code Style

These rules are defaults. Break one when it clearly makes the code better, never only for convenience, and say so when you do.

Rules marked **Hard rule** are never broken.

## Scope of changes

- Existing code that breaks this sheet is debt. Do not imitate it.
- Outside dedicated cleanup work, bring only the lines relevant to the task in line with this sheet.

## Architecture

- Keep things simple. Do not over-engineer.
- Code is split into assemblies. Game-specific assemblies (`Lasertag` and below) reference reusable ones.
- **Hard rule:** reusable assemblies never reference game-specific ones.
- A part of a reusable system gets its own assembly when it brings a dependency the rest should not require (`SharedSpaces` holds the part of `XR` that needs `Netcode`). Deleting that assembly must leave the rest compiling.
- Configuration reaches reusable code through serialized fields in the scene or setup calls from the game assembly.
- Reusable code never exposes a delegate for the game to fill in as a back-door dependency (`public Func<bool> TrackingReady`). Two replacements:
  - The game pushes state in (`provider.TrackingReady = true`). A game class subscribes to the source's change event and pushes again from its handler.
  - The game implements an interface, when the answer must be current at call time.
- Prefer composition over inheritance. An abstract `MonoBehaviour` base class is a last resort: raise it before writing one.
- A class may mix responsibilities while nothing else depends on it. Split it when a second consumer appears or part of it is wanted elsewhere.
- Large classes and functions are fine if everything inside is necessary.
- An interface is earned by two implementations that exist today, or by one that a reusable assembly must not know the concrete type of.
- No runtime third-party dependency for a single feature. Write the feature instead. Editor tooling is exempt.

## Singletons and statics

- Singletons are welcome, including in reusable assemblies. Dependency injection is fine where it fits, but never required.
- Shape: `public static T Instance { get; private set; }`, assigned in `Awake`.
- Several `FindObjectOfType`-style calls for the same object mean it should be a singleton, when the class is ours.
- `FindObjectOfType`-style calls are fine for classes this project does not control, such as `AudioListener`.
- Do not touch singletons or other static state before `Start`.
- Cache a singleton into a private field in `Start` and use the field, so the dependency sits in one place. Do not cache a singleton whose instance can change.
- Use a static class for stateless helpers and for state that needs no scene presence or inspector configuration. Otherwise use a singleton `MonoBehaviour`.
- A missing singleton throws. Do not null-check it.
- Reset all static state in a `RuntimeInitializeOnLoadMethod`. A static event is reset by assigning `delegate { }`.

## Lifecycle and order

- `Awake` sets up the object itself. `Start` reaches other objects.
- Use events or setup calls for initialization order only when `Awake` and `Start` are not enough.
- Use `DefaultExecutionOrder` only for per-frame order.
- Stay subscribed only while enabled, and never subscribe before `Start`:

```csharp
private void Start()
{
	Subscribe();
}

private void OnEnable()
{
	if (didStart)
		Subscribe();
}

private void OnDisable()
{
	roundManager.Respawned -= HandleRespawn;
}

private void Subscribe()
{
	roundManager.Respawned += HandleRespawn;
}
```

- Use `Awaitable` for asynchronous code. Coroutines are legacy.
- An `Awaitable` tied to an object is cancelled with `destroyCancellationToken`.
- Start an `Awaitable` that nothing awaits with `.RunUntilCancelled()`. Cancellation ends it silently and any other exception reaches the console.
- Do not wrap asynchronous work in `try`/`catch (OperationCanceledException)` only to return. Write it as `async Awaitable` and let the caller's `RunUntilCancelled()` handle cancellation.
- Keep the `catch` when cancellation needs a result, cleanup or a state change.

## References

- **Hard rule:** a prefab never references a `GameObject` or `MonoBehaviour` in a scene outside its own hierarchy. References to non-scene objects (prefabs) and assets are allowed. Reverting field overrides must not break anything.
- Prefabs reach the scene through singletons and static events.
- **Hard rule:** renaming a serialized field always carries `[FormerlySerializedAs]`, or every prefab and scene loses its value.
- Use `RequireComponent` with `GetComponent` for a component the class is pointless without. Use `[SerializeField] private` for other references. This one is a leaning, not a rule.

## Events

- Use `Action`, `event` and interfaces.
- `Func` is fine for local use, such as a predicate passed to a method. It is never a public field or property that another system fills in.
- Prefer `Action` and `Func` over custom delegate types.
- C# events are named in the past tense for what happened and start as an empty delegate, so they are invoked without a null check.
- `UnityEvent`s are private serialized fields, capitalized, in the present tense with an `On` prefix. They serve listeners wired in the inspector only. Code subscribes to the C# event.
- Handler methods are named in the present tense with a `Handle` prefix.

```csharp
[SerializeField] private UnityEvent OnRespawn = new();
public event Action Respawned = delegate { };

private void HandleRespawn() { }
```

- When both exist, raise them together. A `UnityEvent` may stand alone when no code subscribes.
- Events that always fire together are one event with an argument.
- A static event raised by instances of a multi-instance class passes the instance as its first argument. Singletons do not.
- Do not poll. Never check a value every frame or in a loop to detect that it changed: raise an event where it changes.
- Three exceptions:
  - Reading a value that changes constantly, such as a moving object's pose.
  - Deferring work to a specific point in the frame, such as a flag set in an input callback and consumed in `Update`.
  - Waiting for a job that outlasts a frame.

## Burst jobs

- Write a Burst job for work that is intensive, frequent and fits Burst's strengths and limitations well.
- A job expected to outlast a frame is waited on with an `Awaitable` that polls for completion.

## Failure

- A broken setup fails loudly. A required serialized field, `GetComponent` result or singleton that is null must throw: no null check, `try`/`catch` or fallback around it.
- Everything else is handled as ordinary logic:
  - A reference that can legitimately be gone during play, such as a despawned player.
  - Outside input: files, network, permissions and device state.
  - Cancellation of an `Awaitable`.

## Networking

- Use SyncBus for networked state that should not live on a `NetworkBehaviour` that is spawned and destroyed, such as a singleton that talks to the network.

## Ask first

Stop and ask before adding any of these:

- A new assembly, or a new reference between assemblies.
- An abstract base class.
- A new package.
- Anything that conflicts with the existing architecture.

## C#

- **Hard rule:** always write explicit types. Never use `var`. Use target-typed `new()` to avoid repeating a type.
- **Hard rule:** never use LINQ in non-editor code.
- Be wary of allocations, above all in per-frame code.
- Do not add `internal` by default. `sealed` is fine. Use `readonly` wherever it applies.
- Tabs, braces on their own lines, block-scoped namespaces.
- A namespace is the assembly's root namespace plus the folder path beneath the assembly.
- Private fields are camelCase with no prefix.
- Always write `private` explicitly, including on Unity messages.
- Use expression-bodied members (`=>`) for one-liners.
- In longer functions, prefer early returns over nested `if`s.
- Numbers: `const` when the value will never change during development, a serialized field when it makes sense to configure, inline otherwise.
- One main type per file, plus its supporting structs and enums.
- Group related fields, events and methods together.
- Members are `private` until something needs them.
- Exposing state:
  - Read from outside, set in code: `public float Health { get; private set; }`.
  - Read from outside, set in the inspector: `[SerializeField] private WeaponVisual visual;` with `public WeaponVisual Visual => visual;`.
  - Written from outside: a public field.
- Use structs for small, plain data. Use a class when the data is large, mutated in place or shared.
- Extension methods are only for types this project does not control. They live in a static helper class in the narrowest namespace that covers their callers. An extension used by one class may sit in that class's file.

## Comments

- Every class gets a brief summary.
- Functions explain themselves by name, however long. No method summaries.
- Comments describe what is, never what was or why an alternative was rejected.

## Tests

- Write new tests only when asked.
- Update existing tests that a change breaks.

## Logging

- Remove logs added for debugging before finishing.

## Undecided

- Logging policy.
- Editor code (`OnValidate`, custom inspectors, menu items): case by case.
