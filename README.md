# 🤖 Robogame

> A personal recreation of [Robocraft](https://store.steampowered.com/app/301520/Robocraft/) built with **C# and Unity**, developed with an emphasis on best practices for 3D and multiplayer game development. Primary development approach: **vibe-coding** — iterative, instinct-driven, AI-assisted.

---

## 📋 Table of Contents

- [Project Overview](#project-overview)
- [Goals](#goals)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Core Systems](#core-systems)
- [Multiplayer Roadmap](#multiplayer-roadmap)
- [Best Practices](#best-practices)
- [Getting Started](#getting-started)
- [Contributing](#contributing)
- [Changelog](#changelog)
- [Reference Docs](#reference-docs)

---

## Project Overview

Robogame is a from-scratch reimagining of Robocraft — a voxel-style robot building and battle game. Players construct robots from modular block components, then battle them in real-time arenas.

This project is a solo dev passion project focused on:
- Deep learning of Unity 3D systems
- Clean, scalable architecture from day one
- Eventual multiplayer support (client-server, authoritative)
- Comprehensive, living documentation

### 👤 Developer Background

**Web dev and game design background — first game dev project.**

I'm comfortable with software architecture, design systems, and frontend patterns, but Unity, C#, and real-time 3D are new territory. This means:

- I'll be learning systems (physics, rendering, netcode) as I go
- I'm relying heavily on AI-assisted / vibe-coding iteration
- **Architectural discipline is non-negotiable** — without it, complexity will spiral fast
- Every system gets documented *as it's built*, not after
- If something feels hacky, it gets flagged and refactored before moving on

> ⚠️ **Spaghetti prevention is a first-class concern.** The best practices in this README are not aspirational — they are enforced. When in doubt, slow down, read the docs, and do it right.

---

## Goals

| Priority | Goal |
|----------|------|
| 🔴 Core | Modular block-based robot construction system |
| 🔴 Core | Physics-based movement (wheels, hover, jets, legs) |
| 🔴 Core | Combat system (weapons, projectiles, damage, destruction) |
| 🟡 Major | Arena / game mode framework |
| 🟡 Major | Basic AI bots for testing |
| 🟢 Future | Online multiplayer (client-server authoritative) |
| 🟢 Future | Matchmaking & lobby system |
| 🟢 Future | Progression / garage system |

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Engine | [Unity 6](https://unity.com/) (LTS) |
| Language | C# 9 (Mono scripting backend, API compatibility: .NET Standard 2.1) |
| Rendering | Universal Render Pipeline (URP) |
| Multiplayer | [Unity Netcode for GameObjects](https://docs-multiplayer.unity3d.com/) *(planned)* |
| Relay / Transport | Unity Gaming Services — Relay + Lobby + UTP *(planned)* |
| Version Control | Git / GitHub (with [Git LFS](https://git-lfs.com/) for binary assets) |
| IDE | VS Code + [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) + [Unity extension](https://marketplace.visualstudio.com/items?itemName=VisualStudioToolsForUnity.vstuc) |
| CI/CD | GitHub Actions *(planned)* |

---

## Architecture

The project is designed around **separation of concerns** and **data-driven design** from the start, to make the eventual multiplayer transition as painless as possible.

> 📝 *Note: "data-driven" here means configuration lives in assets (ScriptableObjects), not in code. This is **not** Unity's [DOTS / ECS](https://unity.com/dots) — we're using classic GameObject/MonoBehaviour, just with disciplined data separation.*

### Key Principles

- **Input is decoupled from logic** — `InputHandler` feeds into a `PlayerController`; bots feed the same interface via `AIController`
- **Server-authoritative mindset** — game state lives in a single source of truth even in singleplayer, so adding Netcode later doesn't require a rewrite
- **ScriptableObject-driven data** — block definitions, weapon stats, movement profiles are all `ScriptableObject` assets, not hardcoded values
- **Code communication via plain C# events** — system-to-system messaging uses C# `event` / `Action` / a custom event bus. **`UnityEvent` is reserved for inspector-wired UI** (buttons, sliders) where its serialization is actually useful — it's too slow and tightly coupled for runtime gameplay logic
- **Explicit dependencies** — prefer constructor/method injection or serialized references over hidden globals. A DI container ([VContainer](https://github.com/hadashiA/VContainer) is the current Unity-native favorite) will be introduced once complexity justifies it
- **Singletons are a last resort** — if used, they must be explicitly bootstrapped (no lazy `Instance` creation), have a clear lifecycle, and never hold gameplay state

---

## Project Structure

```
Assets/
├── _Project/               # All project-specific assets (never mix with third-party)
│   ├── Art/
│   │   ├── Materials/
│   │   ├── Models/
│   │   └── Textures/
│   ├── Audio/
│   ├── Prefabs/
│   │   ├── Blocks/
│   │   ├── Weapons/
│   │   └── UI/
│   ├── ScriptableObjects/
│   │   ├── BlockDefinitions/
│   │   └── WeaponDefinitions/
│   ├── Scenes/
│   │   ├── Bootstrap.unity
│   │   ├── Garage.unity
│   │   └── Arena.unity
│   └── Scripts/
│       ├── Block/          # Block placement, snapping, grid logic
│       ├── Combat/         # Weapons, projectiles, damage
│       ├── Core/           # Bootstrap, service locator, event bus
│       ├── Input/          # Input abstraction layer
│       ├── Movement/       # Drive components (wheel, hover, jet)
│       ├── Network/        # Netcode wrappers (stubbed until needed)
│       ├── Player/         # PlayerController, inventory, loadout
│       ├── Robot/          # Robot assembly, block graph, health
│       └── UI/             # HUD, garage UI, menus
├── Plugins/                # Third-party packages
└── StreamingAssets/
```

---

## Core Systems

### 🧱 Block System
- Each block is a **prefab** with a `BlockBehaviour` component and a `BlockDefinition` ScriptableObject
- Blocks snap to a **uniform grid** on a root robot `Transform`
- The **block graph** tracks connectivity; if the CPU block is destroyed, disconnected blocks detach
- Block types: Structure, CPU, Wheels, Hover, Jets, Weapons, Shields

### 🚗 Movement System
- Movement is component-based — attach `WheelDrive`, `HoverDrive`, or `JetDrive` to a robot
- All drive components implement `IMovementProvider` for a unified interface
- Physics via Unity's `Rigidbody` with custom force application per movement type

### ⚔️ Combat System
- Weapons implement `IWeapon` — `Fire()`, `Reload()`, `CanFire`
- Projectiles use a **pooled** `ProjectilePool` to avoid GC pressure
- Damage events flow through `IDamageable` on block components
- Block destruction triggers a **structural integrity check** on the block graph

### 🎮 Input System
- Built on **Unity Input System** (new)
- `PlayerInputHandler` reads from `InputActionAsset` and exposes clean movement/fire vectors
- Fully mockable for AI and automated testing

---

## Multiplayer Roadmap

> Multiplayer is a **future milestone**, but the codebase is structured to support it from day one.

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Singleplayer foundation with clean server-auth architecture | 🔄 In Progress |
| 2 | Netcode for GameObjects integration, host/client mode (host is authoritative) | 📋 Planned |
| 3 | Unity Relay + Lobby — host-based online play, no dedicated server required | 📋 Planned |
| 4 | Dedicated server build (headless Linux) + matchmaking | 📋 Planned |
| 5 | Lag compensation, client-side prediction & reconciliation | 📋 Planned |

### Multiplayer Design Rules (enforced now)
- **The server (or host) is authoritative** for all gameplay state — clients send inputs, not state changes
- **Authoritative state must be net-serializable** via `NetworkVariable<T>` or `INetworkSerializable` (Netcode for GameObjects), not relying on generic C# serialization
- **Avoid putting gameplay state on `Transform`** — `Transform` is presentation. The source of truth is component data; visuals follow. (e.g. health is a field on a component, not the scale of a healthbar)
- **Physics is server-driven, not deterministic.** Unity's PhysX is *not* deterministic across machines, so we don't pretend it is — the server simulates, clients interpolate/extrapolate. Plan for client-side prediction for the local player only
- **Use RPCs / NetworkVariables** for anything that crosses the network — never reach into another client's components directly

---

## Best Practices

### C# / Unity
- [ ] Use `[SerializeField] private` over `public` fields for inspector-exposed values
- [ ] Prefer `readonly` and `const` wherever possible
- [ ] Avoid `FindObjectOfType` (deprecated in Unity 6) — use serialized references, DI, or `FindFirstObjectByType` / `FindAnyObjectByType` only when truly necessary, and **never in `Update`**
- [ ] Cache component lookups — `GetComponent` in `Update` is a common perf trap
- [ ] Profile before optimizing — use the Unity Profiler, Frame Debugger, and Memory Profiler package
- [ ] Pool frequently instantiated objects with **`UnityEngine.Pool.ObjectPool<T>`** (built-in since Unity 2021) — projectiles, particles, audio sources
- [ ] Use `Addressables` for runtime asset loading *(planned)* — avoid `Resources/`
- [ ] Use **assembly definitions (`.asmdef`)** to enforce module boundaries and speed up compilation
- [ ] Async/await with `Awaitable` (Unity 6) or `UniTask` for async flow — avoid coroutines for new code where async is cleaner

### Code Style
- [ ] PascalCase for classes, methods, properties
- [ ] camelCase with `_` prefix for private fields (`_health`, `_rigidbody`)
- [ ] Interfaces prefixed with `I` (`IWeapon`, `IDamageable`)
- [ ] XML doc comments on all public APIs
- [ ] No magic numbers — use named constants or ScriptableObject values

### Git
- [ ] Conventional commits: `feat:`, `fix:`, `docs:`, `refactor:`, `chore:`
- [ ] Feature branches off `main`; PRs for all merges (even solo) — forces a moment of review
- [ ] `.gitignore` tuned for Unity (Library/, Temp/, Builds/, Logs/, UserSettings/ excluded — use the [official Unity .gitignore](https://github.com/github/gitignore/blob/main/Unity.gitignore))
- [ ] **Git LFS** for binary assets (textures, models, audio, video) — without it, the repo will balloon
- [ ] Force text serialization for assets: **Edit → Project Settings → Editor → Asset Serialization → Force Text** (already default in modern Unity, but verify)
- [ ] Enable **Visible Meta Files** in the same panel (also default — but `.meta` files MUST be committed)

---

## Getting Started

### Prerequisites

- Unity 6 (LTS) — install via [Unity Hub](https://unity.com/unity-hub)
- .NET SDK (bundled with Unity)
- Git

### Setup

```bash
git clone https://github.com/Greyson314/robogame.git
cd robogame
# Open the project root in Unity Hub → Add project from disk
```

> ⚠️ On first open, Unity will import all assets. This may take several minutes.

### Running

1. Open `Assets/_Project/Scenes/Bootstrap.unity`
2. Press **Play** in the Unity Editor

---

## Contributing

This is a solo project, but notes for future collaboration (or future me):

1. Fork and branch from `main`
2. Follow the code style and best practices above
3. Write XML doc comments for any new public API
4. Update this README and relevant docs when adding a new system
5. Open a PR with a clear description of changes

---

## Changelog

All notable changes are documented here.

### [Unreleased]
- Initial project setup
- README and documentation scaffold

---

## Reference Docs

- [docs/ROBOCRAFT_REFERENCE.md](docs/ROBOCRAFT_REFERENCE.md) — design research on the original Robocraft (systems, weapons, damage model, monetization lessons, what to borrow vs. avoid)

---

*Last updated: April 29, 2026*
