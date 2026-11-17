# UI and UX review

## Current interface and user journey

The app's underlying workflow has useful tools, but a first-time user must understand a dense map setup screen: import, analyze, show original, edit annotations, calibrate scale, optionally calibrate geography, then preview/navigate. `MapSetupController` owns a large portion of this workflow and binds many serialized controls. Navigation offers room selection, route/build controls, GPS and map camera actions. Developer seed/smoke controls exist in development contexts.

The scenes use UGUI `Text`, `InputField`, button/dropdown/slider controls and a safe-area layout helper. `MinimalMobileUi` and `ARNavSceneSetup` indicate the interface is still compact/prototype-oriented. Source proves capabilities exist, but actual visual fit and touch target size need screenshots/device verification.

## Main usability gaps to resolve

- There is no clearly evidenced guided checklist that explains required versus optional stages (image, geometry review, scale, route preview).
- Automatic analysis can change source geometry and clears old derived rooms/walls/doors; user confirmation and recovery should be prominent before destructive regeneration.
- Generic status text can be insufficient for long-running analysis/build operations; there is no evidence in inspected controller code of progress percentage or cancel for extraction.
- GPS calibration can be mistaken for indoor positioning. Label the feature accurately and explain indoor location limitations.
- Destination selection and route start should clarify current location source: manually selected start, simulated waypoint or live position.
- Legacy controls and small text can be difficult on high-density/small Android screens; test font scaling, keyboard and system insets.
- Empty, invalid image, unsupported format, extraction failure, disconnected route and GPS-denied states should show next actions, not just failure text.
- Provide app-level Android back behavior and preserve unsaved annotation edits or explicitly ask to discard.
- Color, labels and line style must remain distinguishable in bright settings, for color-vision deficiency and at small zoom.

## Recommended visual system

- **Canvas:** warm neutral background with a low-contrast grid; use an uncluttered 2D plan as the primary editing surface.
- **Structure:** dark navy walls; room regions in restrained pale tints; doors drawn with clear openings; selected room uses a strong outline and label.
- **Route:** saturated blue/teal with a separate dashed/high-contrast style option; start and destination use different shape/icon as well as color.
- **Controls:** Material-like bottom sheet/cards, one prominent next action, compact secondary actions grouped into contextual panels. Keep map interaction unobstructed.
- **Typography:** scalable sans-serif with clear hierarchy; avoid dense diagnostic text in end-user screens.
- **Touch:** target 48 dp minimum where practical, visible pressed/selected/disabled states, undo available after editing, no precision-only tiny handles.
- **Motion:** short easing for panel changes, selection and route reveal; respect reduced-motion preference and avoid animated geometry during heavy generation.
- **Feedback:** explicit loading state with stage label, cancel when safe, result summary and actionable validation errors.
- **Accessibility:** semantic labels, high contrast, font scaling, non-color cues, screen-reader order and touch target checks.

## Suggested screen organization

1. **Maps home:** recent maps, create/import action, rename/delete/export in menu.
2. **Map setup:** image workspace and a compact progress stepper (`Import → Review → Scale → Ready`). Keep geographic calibration under advanced tools.
3. **Review editor:** room/wall/door tool palette with undo/redo, selected-room properties and a source-image opacity toggle.
4. **Navigation preview:** destination search/list and route summary in bottom sheet; map gets 2D/3D, fit and reset-camera controls; clearly expose start mode and route status.

## Testing needed

Capture screenshots at smallest supported phone, tall aspect ratio, tablet (even if currently unsupported), font scale 1.3–2.0, status/navigation bars, keyboard visible and light/dark system appearance. Measure touch targets in dp and run TalkBack manually. No visual/device measurements were made in this audit.
