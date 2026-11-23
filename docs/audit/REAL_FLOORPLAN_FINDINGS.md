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

Android visual quality and interaction require an installed build and device walkthrough; an Edit Mode mesh build alone does not prove that either real plan produces a fully accurate final model.
