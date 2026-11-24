# Real floorplan follow-up (2026-11-24)

The two images available in Downloads were inspected locally and were not copied into the repository. The landscape source is 640 × 480 pixels; the portrait PDF-page capture is 477 × 642 pixels. Their paired screenshots show the prior dense 3D extrusion and page-filled floor. These JPEGs are too small and compressed to guarantee every architectural feature, particularly the pale balcony outline in the landscape drawing.

## Confirmed causes and changes

- The wall-mask text pass protected long strokes before examining components. Long furniture, car and plant outlines therefore survived as walls. A second pass now rejects compact detached components while retaining large wall networks, narrow structural strokes and sizeable detached structures. Dense drawings also omit very short derived 3D paths while retaining source mask runs for review.
- The clean-grid shortcut accepted a line with 55% span coverage, then generated a full-span wall. It now requires 90% coverage; opening detection retains its own 55% threshold.
- Localized room extraction rasterized raw image ink, so furniture cut holes in the floor. It now uses the cleaned architectural mask. Temporary wall bridges distinguish interior space from exterior through plausible openings without modifying the saved wall mask.
- When an open perimeter still connects all interior space to the page background, the extractor produces a low-confidence `EstimatedInterior` floor from the wall envelope. It is explicitly marked for review and is never presented as measured or certain geometry.
- A conservative cluster of full-height page rules now crops a right-side title block before extraction. The detected coordinates remain in the original image coordinate system.
- The 3D mobile screen now uses one bottom action row and relies on the existing orbit/pinch gestures; the redundant rebuild button and large angle/scale sliders were removed. Import, JSON copy/export, GLB export, GPS, top view, fit and reset remain visible.

## Local image probe

An uncommitted, temporary Unity Edit Mode probe loaded both Downloads images, extracted geometry, converted it to local metres and built wall meshes. The landscape image produced 12 wall paths and one explicitly estimated interior floor; the portrait image produced 202 wall paths after short-fragment filtering. Both wall meshes built successfully. The probe and image files were not committed.

## Known limits

The landscape balcony boundary is light gray, reaching roughly RGB 190–230 at this resolution; several of its strokes are below the dark-wall threshold. The portrait plan still has attached furniture/fixture outlines that share raster components with walls, plus many short hatch strokes. These require review and correction in the existing geometry editor. The fallback estimated floor can include area outside an irregular footprint, so its boundary must be reviewed before treating it as architectural data.

Unity 6000.6.0f1 built the Android APK successfully (0 errors, 23 warnings), and `adb install -r` succeeded on the connected device without clearing app data. The phone disconnected while locked before the app screen could be inspected. Android visual quality and interaction therefore still need an unlocked-device walkthrough; an Edit Mode mesh build alone does not prove that either real plan produces a fully accurate final model.

## OpenCV replacement probe (same date)

The prior findings above describe the legacy detector. The new Android detector was run directly on both original Downloads JPEGs using OpenCV 4.14.0's official Windows Java binaries and the same `OpenCvFloorplanProcessor.java` as the Android build. Mask images were inspected locally; they remain ignored under `Logs/` rather than committing the user's drawings.

- Landscape (640 × 480): 10,553 structural-mask pixels and 155,222 floor-mask pixels. The resulting mask keeps the main rectangular floor, bathroom/bedroom partitions, hallway boundary and bottom exterior wall. A bathroom doorway gap remains open. The balcony's pale outline is absent because it is not black, and its floor is consequently not reconstructed.
- Portrait (477 × 642): document crop `(32, 53, 297, 557)` excludes the right title block and page border. The mask has 12,527 wall pixels and 135,926 floor pixels. The central living area is a continuous floor, and car/plant drawings are largely excluded. A stair hatch region is hollowed instead of becoming a filled raised block. Some attached kitchen/fixture outlines remain, and some short structural edges are uncertain.

These are **mask observations**, not a verified Android 3D rendering. The generated Unity model and GLB still require a device pass with both images. Earlier wall height was only 0.35 m; it is now 2.7 m, but the new Android preview has not yet been visually verified.
