# CloSim Online Multiplayer — Integration Checklist

> Owned by the **Build/QA & Integration Harness**. Additive tooling doc — describes how A1
> integrates the feature branches back into `feature/online-multiplayer` and how the **user**
> verifies each phase in the Unity editor (agents cannot compile/run Unity here).
>
> Source of truth for types/flows: `Documentation/online/architecture.md`. Rules: `CLAUDE.md`.
> Ownership: `AGENTS.md`.

---

## 0. What this harness ships (all NEW files, additive)

| File | Purpose |
|------|---------|
| `Assets/Editor/Build/CloSimBuild.cs` | Headless Windows build entry point (`CloSimBuild.BuildWindows`). |
| `Tools/build-windows.ps1` / `.sh` | Wrapper scripts that invoke Unity in batch mode. |
| `Assets/Tests/EditMode/CloSim.Tests.EditMode.asmdef` + `ContractsSmokeTests.cs` | EditMode NUnit smoke test over the `Online.Contracts` DTOs. |
| `Assets/Scripts/Online/Contracts/Online.Contracts.asmdef` | **Enabler** — makes the contracts a referenceable assembly so the test can compile (see §5). |
| `.github/workflows/ci.yml` | GitHub Actions: EditMode tests + Windows build (game-ci). |
| `Documentation/online/integration-checklist.md` | This file. |

---

## 1. Recommended merge order (into `feature/online-multiplayer`)

Merge in **dependency + risk order**, landing the riskiest shared refactor first so later
branches rebase onto already-generalized code instead of fighting it:

```
1. A2  feat/netcode-foundation   → adds Mirror + connection API. Unblocks everyone.
2. A4  feat/gameplay-sync        → de-hardcodes LoadMatch 4→N (the big shared refactor). Land it early.
3. A3  feat/rooms-lobby          → lobby/rooms + PlayMode↔NetworkMatchConfig adapter, on top of N-slot LoadMatch.
4. A5  feat/master-server        → in-game Server List UI (needs A2's IMasterServerClient + connect).
   ·  Server/ backend (also A5)  → INDEPENDENT: language-agnostic, no Unity dep — merge any time.
```

**Rationale**
- **A2 first**: everything references `IOnlineConnection` / `IMasterServerClient`; Mirror must be in
  `Packages/manifest.json` before A3/A4/A5 compile.
- **A4 before A3**: A4 owns the `LoadMatch.cs` 4→N de-hardcode — the single biggest merge hot-spot.
  Landing it first means A3 (which maps `RoomMemberSlot` roster → `LoadMatch` slots) rebases onto the
  already-generalized N-slot arrays instead of colliding with them.
- **A3 after A4**: rooms/lobby consume the N-slot model and add the `PlayMode ↔ NetworkMatchConfig`
  adapter; cleaner once the count-based match model exists in `LoadMatch`.
- **A5 last (UI)**: the Server List UI depends on A2's connect + `IMasterServerClient` and A1's
  `RoomInfo`. A5's **backend** in `Server/` has no Unity dependency and can merge whenever ready.

After each merge: A1 re-runs the offline regression check (§4) and the EditMode tests (§3) before
merging the next branch. Every feature branch should `merge feature/online-multiplayer` into itself
first to pick up siblings' landed work.

---

## 2. Expected merge conflict hot-spots

| File / area | Who touches it | Why it conflicts | Mitigation |
|-------------|----------------|------------------|------------|
| `Assets/Scripts/Core/LoadMatch.cs` | **A4** (4→N de-hardcode) | Slot arrays `[4]`, `GetPlayerCount()`, `IsPlayerBlue()`, `UsesFourWaySplit()`, `PairInputs()`. Any other branch that reads slots will conflict. | Merge A4 first (§1). Keep A4's edits minimal + behavior-preserving for offline. |
| `Core.PlayMode` / `Enums.cs` | **A3** adapter | If A3 adds online-only shapes or maps enum→counts near the enum. | Prefer a **new** adapter file (`Online.Rooms`) over editing the enum; keep the enum untouched if possible. |
| `Packages/manifest.json` | **A2** (adds Mirror via OpenUPM `com.mirrornetworking.mirror`) | Single-line JSON dep list; any other branch that adds a package conflicts. | Only A2 edits it. Others rebase after A2 lands. This harness deliberately does **not** touch it. |
| `ProjectSettings/EditorBuildSettings.asset` | A2 (test scene), A5 (Server List scene) | Adding scenes to the build list. | Coordinate scene-list additions through A1; resolve by union. |
| `Assets/Scripts/Online/Contracts/Online.Contracts.asmdef` (this harness) | **This harness only** | New assembly for the contracts. Safe (predefined `Assembly-CSharp` still auto-references it), but if A1/A2 also add a Contracts asmdef → duplicate-name collision. | Single source: this file. If another agent adds asmdefs to `Online.*`, they should **reference** `Online.Contracts` rather than re-create it. See §5. |
| `.meta` GUIDs for new folders (`Assets/Tests`, `Assets/Editor/Build`) | This harness | Two branches independently creating the same folder get different folder-meta GUIDs. | These folders are unique to this harness; low risk. If duplicated, keep one meta. |

---

## 3. Running the EditMode tests

**In the editor (fastest feedback):**
1. Open the project in Unity **2023.2.22f1** (Unity Hub).
2. `Window > General > Test Runner`, select the **EditMode** tab.
3. Click **Run All**. The `CloSim.Tests.EditMode` assembly runs `ContractsSmokeTests`
   (capacity math, ≤3 per alliance, ≤6 total, blank master-server config, no token on `RoomInfo`).

**Headless / CLI (what CI runs):**
```bash
"<Unity.exe>" -runTests -batchmode -projectPath "<repo root>" \
    -testPlatform EditMode \
    -testResults "test-results/editmode.xml" \
    -logFile "-"
```
Exit code 0 = all passed. Results XML lands at `test-results/editmode.xml` (NUnit3 format).

**In CI:** `.github/workflows/ci.yml` → job **EditMode Tests** (game-ci/unity-test-runner) runs this on
every push/PR once the `UNITY_LICENSE` secret is provided (§6).

---

## 4. Building the Windows player

**Exact batchmode command** (what `Tools/build-windows.ps1` runs):
```
"<Unity.exe>" -batchmode -quit -nographics \
    -projectPath "<repo root>" \
    -executeMethod CloSimBuild.BuildWindows \
    -logFile "-" \
    -buildOutput "Build/Windows/CloSim.exe"
```
- `<Unity.exe>` for this project: `C:\Program Files\Unity\Hub\Editor\2023.2.22f1\Editor\Unity.exe`.
- Wrappers: `pwsh ./Tools/build-windows.ps1` or `./Tools/build-windows.sh` (both auto-detect Unity or
  accept `-UnityPath` / `$env:UNITY_PATH`). Add `-development` / `DEVELOPMENT=1` for a dev build.
- Output: `Build/Windows/CloSim.exe`. Scenes come from **File > Build Settings** (build fails clearly
  if none are enabled).

---

## 5. Unity-editor verification (per phase — the USER runs these)

**Setup (every phase)**
1. Open the project in Unity **2023.2.22f1** (URP, Input System 1.7.0). Let it import.
2. Wait for **Mirror** to resolve (added by A2 via OpenUPM `com.mirrornetworking.mirror` in
   `Packages/manifest.json`). If A2's brief instead documents a **Git URL** or **Asset Store / OpenUPM
   manual** import, follow that note in `Documentation/online/agents/A2-netcode-foundation.md`.
3. Confirm the Console shows **no compile errors** before testing.
   - **Assembly note:** this harness adds `Online.Contracts.asmdef`, so the DTOs now compile into an
     `Online.Contracts` assembly. Predefined `Assembly-CSharp` still auto-references it, so gameplay and
     the other agents' `Online.*` code keep seeing the contracts. If a sibling later wraps its code in
     its **own** asmdef, that asmdef must add `Online.Contracts` to its `references` list.

**Phase 1 — Netcode (A2)**
1. Open A2's test bootstrap scene (`Assets/Scenes/Online/NetcodeTest.unity`) or drop A2's netcode
   dev-harness component onto a throwaway scene.
2. Enter **Play** mode → **Start Host**. Confirm the listen-server comes up (no errors, host state).
3. Second instance (ParrelSync clone or a standalone build) → **Start Client** to `127.0.0.1` → connects.
4. **LAN + token:** Start Host with a **join token**, Start Client from another machine on the LAN with
   the same token → joins; with a **wrong token** → rejected as `ConnectResult.Rejected_BadToken`.
5. Stop Host while a client is connected → client raises `OnClientDisconnected`.

**Phase 2 — Rooms & Modes (A3)**
1. Host **Create Room** (in-game UI); second instance **Join**.
2. Verify **6-cap** and **≤3 per alliance** enforcement, **ready-up**, and **host-picks-mode** replicate
   across instances. Confirm the `PlayMode ↔ NetworkMatchConfig` adapter yields the right counts.

**Phase 3 — Gameplay Sync (A4)**
1. Launch a networked match with N players (e.g. **3v3**).
2. Verify each client **owns exactly its robot**, sees others move (`NetworkTransform`), and that
   `FieldScorer` / `Fms` timer read **identical across clients** (host-authoritative).
3. Confirm the **single-view online camera** (not split-screen) and that **offline split-screen still
   works unchanged** (§ regression below).

**Phase 4 — Master Server + Server List (A5)**
1. Run the `Server/` backend locally.
2. Set the master-server URL to the **local** backend **only in your editor** (never commit it — §6).
3. Host a **public** room → it appears in the in-game **Server List** → another client **joins from the
   list**. Verify heartbeat/TTL drop when the host stops.
4. Once the real AWS endpoint is filled in (`MASTER_SERVER_URL`), the same flow browses the **public** list.

**Offline regression check (run after EVERY merge)**
- Play the existing **Rebuilt** and **Reefscape** scenes in local split-screen (1–4 players) and confirm
  **no behavioral change** vs. `main`.

---

## 6. Known blank config — `// TODO: user provides` placeholders

Everything below ships **blank on purpose** (golden rule 2). The user fills these at deploy time; never
commit a real value.

| Placeholder | Where | Filled by user with |
|-------------|-------|---------------------|
| `MasterServerConfig.MasterServerUrl = ""` | `Assets/Scripts/Online/Contracts/IMasterServerClient.cs` | The AWS master-server (directory API) HTTPS endpoint. Blank ⇒ `IsConfigured == false`, no calls made, Server List shows "master server not configured". |
| `MasterServerConfig.ClientApiKey = ""` | same file | Client credential that gates the directory API (API key / signing secret). |
| `UNITY_LICENSE` | GitHub repo secret (`.github/workflows/ci.yml`) | Contents of the Unity `.ulf` license file (see game.ci activation docs). |
| `UNITY_EMAIL` / `UNITY_PASSWORD` | GitHub repo secrets | Unity account creds for CI activation (+ `UNITY_SERIAL` for Pro). |
| `UNITY_PATH` (build scripts) | env var or `-UnityPath` arg | Full path to `Unity.exe` 2023.2.22f1 (only if auto-detect misses it). |
| AWS infra values (region, bucket, ARNs, endpoint) | A5's `Server/` IaC | Left as blank placeholders in A5's backend — user provides at deploy. |

The EditMode smoke test (`ContractsSmokeTests.MasterServerConfig_ShipsBlank_...`) **asserts** the two
config strings are empty, so CI fails fast if a real endpoint or key is ever committed.
