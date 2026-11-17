# Development roadmap

This plan is based on repository evidence. No implementation is performed by this audit. Each task should be a small pull request/commit with a specific device acceptance result.

## Stage 1 — Stability and bug fixes

| Priority | Task / systems | Outcome and dependencies | Difficulty / risk | Acceptance and Android test |
| --- | --- | --- | --- | --- |
| P0 | Establish baseline build and log checklist: all scenes, runtime scripts, Java picker. | Known compile/runtime state; no code dependency. | Easy / Low | Clean import/build in Unity 6000.6; install and reach MapSetup; save logcat and device/API details. |
| P0 | Set final Android application ID and explicit target SDK: `ProjectSettings/ProjectSettings.asset`, Android build profile. | Stable release identity and predictable platform behavior. | Easy / Medium | Inspect APK manifest; install/update test; verify target API and SAF/location behavior. |
| P1 | Add import dimension/decoded-memory limits and actionable errors: `UnityImageImportService`, `MapImageStorageService`, `MapSetupController`. | Prevent unbounded image decode/analysis pressure. | Medium / Medium | Import small, max-accepted, oversized, corrupt, renamed and unsupported files on phone; ensure app remains responsive and reports next steps. |
| P1 | Make JSON persistence recoverable: `LocalFileMapRepository`, serializer and tests. | Protect user maps from interrupted writes. | Medium / Medium | Repeated save/reopen, malformed JSON, simulated failed temp/replace; verify recovery and no data loss. |
| P1 | Verify and define back/lifecycle flows: `NavigationController`, setup controller, picker, location provider. | Predictable Android back, pause/resume and cancellation. | Medium / Medium | Back from Navigation, picker cancel, background/restore during import, revoke location permission and resume. |

## Stage 2 — Usability and visual improvements

| Priority | Task / systems | Outcome and dependencies | Difficulty / risk | Acceptance and Android test |
| --- | --- | --- | --- | --- |
| P1 | Add setup stepper, explanatory labels and operation states in `MapSetupController` UI. | Users understand required stages and optional geo calibration. Depends on baseline flow. | Medium / Low | First-time user completes import/review/scale without developer explanation; test phone font scaling. |
| P1 | Apply visual system and responsive touch layouts across MapSetup/Navigation scenes. | Readable mapping interface and consistent states. | Medium / Medium | Screenshot review at min phone resolution, safe areas, keyboard and large text; tap all controls comfortably. |
| P2 | Improve annotation selection/snapping/undo feedback in `FloorPlanAnnotationController` and overlays. | More reliable editing on touch. | Medium / Medium | Add/edit/delete/undo room, wall and door on phone; geometry remains valid after save/reopen. |
| P2 | Add explicit loading/progress/cancel/retry states for import, analysis, generation. | No apparent frozen app; clear error recovery. Depends on Stage 1 processing work. | Medium / Medium | Interrupt/cancel at safe points; UI always returns to usable state. |

## Stage 3 — Navigation improvements

| Priority | Task / systems | Outcome and dependencies | Difficulty / risk | Acceptance and Android test |
| --- | --- | --- | --- | --- |
| P1 | Display route distance and concise destination/start status: `NavigationRoute`, `NavigationController`. | Route has meaningful feedback using existing distance. | Easy / Low | Route distance matches test fixture polyline; clear route resets values. |
| P1 | Improve route contrast, start/destination markers and 2D/3D camera reset. | Route readable and map recoverable after gestures. | Medium / Low | Test bright/dim room, zoom extremes, camera reset and selected destination persistence. |
| P2 | Add room search/filter and room information panel. | Faster destination discovery. Depends on naming/data quality. | Medium / Low | Search rooms by display name/number, clear no-result feedback, route selection remains correct. |
| P2 | Expand route geometry regression fixtures and measure nav grid scaling. | Detect wall-crossing/unreachable regressions. Depends on repeatable test infrastructure. | Medium / Medium | Test route through doorway, blocked wall, invalid geometry and large map on phone; compare route length/status. |
| P3 | Add route accessibility costs and multi-floor connectors. | Accessible/multi-floor navigation. Requires product data model and connector editing. | Hard / High | Stair-free route chosen when available; elevator/connector transition shown; multi-floor fixture coverage. |

## Stage 4 — Advanced functionality

| Priority | Task / systems | Outcome and dependencies | Difficulty / risk | Acceptance and Android test |
| --- | --- | --- | --- | --- |
| P1 | Add map library actions and importable project package. | Users can manage and share authored maps. Depends on robust schema/persistence and SAF. | Medium / Medium | Export/import project on two devices; image and geometry match and invalid archive rejected safely. |
| P2 | Add extraction confidence/review and improved doorway detection suggestions. | Less manual effort without hiding uncertain output. Depends on labeled sample plans and evaluation fixtures. | Hard / High | Report precision/recall on representative plans; user can correct every suggestion. |
| P2 | Add bookmarks and offline mode indicators. | Revisit destinations without network dependence. Depends on map/floor identifiers. | Easy–Medium / Low | Create, edit, delete bookmark; navigate with airplane mode. |
| P3 | Add QR checkpoints or indoor positioning pilot. | Better start-position estimate at equipped venues. Requires chosen approach and privacy/venue setup. | Medium–Hard / High | Known checkpoint error measured in controlled venue; show low-confidence and lost-position states. |
| P3 | Evaluate ARCore only after stable mapping and calibration. | Optional world-anchored visualization. Requires AR Foundation/ARCore and device pilot. | Hard / High | Supported phone maintains alignment over a defined route; unsupported device falls back cleanly. |

## Stage 5 — Optimization and release readiness

| Priority | Task / systems | Outcome and dependencies | Difficulty / risk | Acceptance and Android test |
| --- | --- | --- | --- | --- |
| P0 | Profile import, analysis, generation, NavMesh and idle rendering on mid-range reference phone. | Measured budgets, no invented performance claims. | Medium / Low | Record median startup/frame time, peak managed/native memory, GC, build size and thermal behavior with repeatable plan. |
| P1 | Optimize extraction and grid using measured hotspots. | Responsive large-map handling. Depends on profiler baseline and fixture outputs. | Hard / Medium | No frame stall beyond agreed budget on max supported plan; outputs remain fixture-equivalent. |
| P1 | Add accessibility and device compatibility pass. | Readable, operable app across supported density/font sizes. | Medium / Low | Touch targets, TalkBack order, contrast, font scale, safe area and Android API matrix pass. |
| P1 | Release build/signing, merged manifest, privacy and dependency/build-size review. | Distribution-ready artifact. Depends on final app ID/SDK. | Medium / Medium | Install release APK/AAB; verify identity, permissions, ABI, signature, package size and update path. |

## Sequencing note

Keep feature branches narrow. The most useful first slice is baseline verification followed by import limits and resilient persistence. Advanced features should wait until routes, scale, image handling and map storage have repeatable phone tests.
