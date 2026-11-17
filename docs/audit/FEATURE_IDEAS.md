# Feature feasibility and technical direction

Complexity is relative to the present codebase and a solo student developer. Existing functionality is identified first to avoid rebuilding it unnecessarily.

## Map creation and editing

| Idea | Current baseline | Value / dependencies / limits | Complexity |
| --- | --- | --- | --- |
| Import floor plan images | Implemented PNG/JPEG Editor and Android SAF pathways. | Improve size limits, orientation metadata, duplicate/error UX and device tests. Arbitrary PDF/vector import is not present. | Easy–Medium |
| Better automatic 2D-to-3D | Heuristic wall mask, candidate rooms, wall path and mesh generation already exist. | Improve with confidence scores, user review and tuned algorithms first. ML/CV would add dataset, model size, licensing and device inference costs; no guarantee on symbols/styles. | Hard |
| Wall/door detection | Wall-like raster runs and manual door annotation exist; robust automatic door semantics are not established. | Door gaps are hard to infer from noisy plans; use detected suggestions plus manual correction. | Medium–Hard |
| Interactive floor-plan editor | Room/wall/door tools, selection, rename, undo/redo, delete already exist. | Improve precision, snapping, properties and usability; avoid a rewrite. | Medium |
| Drag/drop room/waypoint placement | Pointer-driven editing and room anchors exist. | Make affordances and snapping explicit; validate touch hit-testing and prevent accidental pan/edit. | Medium |
| Automatic room labeling | Candidate room extraction and display names exist; semantic text recognition not established. | OCR adds model/locale/dependency complexity. Prefer room-number/name entry; OCR suggestions only if source text is useful. | Hard |
| Adjustable wall heights/scale | Scale calibration exists; wall presentation/generation configs exist. | Expose safe per-map wall height and validate geometry/navmesh consequences. | Easy–Medium |
| Save/reopen custom maps | JSON and app-private project/image storage exist. | Map library UI, migration/versioning, robust atomic saves, backup/export are needed. | Medium |
| Multiple floors | Project model contains floor collections. | Requires floor selector, per-floor geometry and connectors (stairs/elevator), cross-floor route graph and transition UX. Data support alone is not end-to-end support. | Hard |

## Navigation

| Idea | Current baseline | Value / dependencies / limits | Complexity |
| --- | --- | --- | --- |
| Automatic shortest route | NavMesh complete-path calculation exists. | Add fixture coverage and destination UI; current path is shortest under NavMesh area costs, not necessarily accessibility-aware. | Easy–Medium |
| Room search/destination | Navigation scene has destination dropdown. | Add search/filter and useful names when map library grows. | Easy |
| Route highlighting | LineRenderer route and waypoint objects exist. | Improve contrast, occlusion, animated progress and accessible alternative patterns. | Easy–Medium |
| Animated arrows | Not evidenced as implemented. | Route mesh/LineRenderer can support directional material/markers; avoid per-frame allocations. | Medium |
| Distance/time | Total path distance exists in route data. | Display distance; estimate time only with configurable walking speed and label it approximate. | Easy |
| Accessible routes avoiding stairs | No complete floor-connector/accessible graph flow evidenced. | Need connector metadata, edge costs/accessibility constraints, and route tests. | Hard |
| Automatic rerouting | No live reroute loop evidenced. | Needs reliable indoor position source and hysteresis to prevent route oscillation. GPS alone is usually inadequate indoors. | Hard |
| Floor switching | No complete navigation flow evidenced. | Depends on explicit vertical connectors and multi-floor graph. | Hard |

## 3D visualization

| Idea | Current baseline | Value / dependencies / limits | Complexity |
| --- | --- | --- | --- |
| Orbit/pan/zoom/reset | Camera framing and preview controllers exist. | Verify gestures with touch and provide obvious reset; separate camera gestures from map editing. | Medium |
| 2D/3D toggle | Source image/structured map and generated 3D preview paths exist, but a polished synchronized toggle is not confirmed. | Preserve selected room/route across view changes and maintain a shared coordinate mapping. | Medium |
| Materials/lighting | URP and wall/floor materials exist. | Mobile-friendly light/shadow choices; test route readability and overdraw. | Easy–Medium |
| Room highlight/info | Room identities/anchors exist. | Add selection state, labels and information panel. | Medium |
| Transparency/cutaway | Not evidenced. | Shader/material feature and occlusion design; transparent sorting can be costly/confusing. | Medium |
| Animated routes | Route line exists. | Animate a shader offset or pooled markers; respect reduced motion and keep route legible. | Medium |
| Room details | Structured room data exists. | Add editable room attributes (name/type/accessibility) and a contextual card. | Medium |
| First-person walkthrough | NavMesh exists. | Camera/controller, collision, transitions and comfort settings required; not a navigation-position substitute. | Medium–Hard |

## Advanced features

| Idea | Feasibility and dependencies | Complexity / limitations |
| --- | --- | --- |
| ARCore navigation | Requires AR Foundation + ARCore XR plugin, device support, camera permission, tracking calibration, coordinate anchoring and substantial UX. Existing Unity AR packages are not listed. | Hard; route-to-world alignment and indoor visual tracking are difficult; should follow stable 2D/3D maps. |
| QR position calibration | QR scanner/decoder and venue-placed codes plus map-to-code transform. No QR implementation found. | Medium; good for discrete checkpoints, not continuous tracking between codes. |
| Offline navigation | Core project and route data are local; route display can work offline when map loaded. | Easy–Medium to formalize; avoid online-only asset dependencies and include offline status. |
| Map sharing/export | GLB export and JSON clipboard path exist; a complete importable/shareable project package is not established. | Medium; package JSON + image + schema/version validation, user-selected SAF export/import. |
| Bookmarks | No persistent saved locations surfaced. | Easy–Medium; store project/floor point IDs with schema migration. |
| Voice guidance | No speech feature found. | Medium; Android TTS, accessibility controls and localization; voice route instructions need stable route semantics. |
| Indoor positioning | GPS abstraction exists but indoor reliability is poor. Alternatives: manual start, QR checkpoints, BLE beacon/Wi-Fi infrastructure, UWB or AR visual positioning. | Medium–Hard; hardware/venue infrastructure, calibration, privacy and error handling dominate. |
| Mini-map | Current plan/map visualization can be adapted. | Medium; synchronize camera bounds and route state without duplicating route computation. |

## Recommended order

Improve existing map persistence, detection review, route presentation and device responsiveness before adding AR or live indoor positioning. A practical high-value vertical slice is: import an image → review/edit rooms/doors → calibrate scale → save/reopen → choose room → display measured route distance in a synchronized 2D/3D view.
