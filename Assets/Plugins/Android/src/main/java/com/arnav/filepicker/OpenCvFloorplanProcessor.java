package com.arnav.filepicker;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;
import org.opencv.android.OpenCVLoader;
import org.opencv.core.Core;
import org.opencv.core.CvType;
import org.opencv.core.Mat;
import org.opencv.core.MatOfPoint;
import org.opencv.core.Rect;
import org.opencv.core.Scalar;
import org.opencv.core.Size;
import org.opencv.imgproc.Imgproc;

/** Offline OpenCV preprocessing. All returned coordinates use the input image pixels. */
public final class OpenCvFloorplanProcessor {
    private static final int MAGIC = 0x4f435650; // OCVP
    private static final int VERSION = 1;
    private OpenCvFloorplanProcessor() {}

    /**
     * Returns a 32-byte little-endian header followed by wall and floor masks.
     * Header: magic, version, crop x/y/width/height, wall width in pixels, flags.
     * Input is the maximum RGB channel per pixel so coloured ink cannot pass as black.
     */
    public static byte[] process(byte[] blackness, int width, int height) {
        if (blackness == null || width < 32 || height < 32 ||
                (long) width * height > 8000000L || blackness.length != width * height)
            throw new IllegalArgumentException("Invalid floorplan image dimensions.");
        if (!OpenCVLoader.initLocal()) throw new IllegalStateException("OpenCV native library is unavailable.");
        final List<Mat> owned = new ArrayList<>();
        try {
            Mat gray = own(owned, new Mat(height, width, CvType.CV_8UC1));
            gray.put(0, 0, blackness);
            Mat blurred = own(owned, new Mat());
            Imgproc.GaussianBlur(gray, blurred, new Size(3, 3), 0);
            Mat otsu = own(owned, new Mat());
            double otsuLevel = Imgproc.threshold(blurred, otsu, 0, 255, Imgproc.THRESH_BINARY_INV | Imgproc.THRESH_OTSU);
            Mat black = own(owned, new Mat());
            // Black is required for architectural walls. Otsu adapts to scan quality,
            // while the cap excludes pale furniture and document shading.
            Core.inRange(blurred, new Scalar(0), new Scalar(Math.min(150, Math.max(70, otsuLevel + 18))), black);
            Mat thinBlack = own(owned, new Mat());
            Core.inRange(gray, new Scalar(0), new Scalar(110), thinBlack);
            Core.bitwise_or(black, thinBlack, black);
            byte[] source = new byte[width * height]; black.get(0, 0, source);
            Rect crop = detectDocumentCrop(source, width, height);
            Mat inCrop = own(owned, black.submat(crop));
            // Keep the thresholded component intact here. A 3x3 opening erased
            // valid one-pixel edges from thin partitions and reduced recall on
            // the synthetic 6px walls. Connected-component classification below
            // removes isolated specks without shrinking connected architecture.
            Mat strong = own(owned, inCrop.clone());
            int lineLength = Math.max(18, Math.min(crop.width, crop.height) / 12);
            Mat horizontal = own(owned, new Mat());
            Mat vertical = own(owned, new Mat());
            Mat horizontalKernel = own(owned, Imgproc.getStructuringElement(Imgproc.MORPH_RECT, new Size(lineLength, 1)));
            Mat verticalKernel = own(owned, Imgproc.getStructuringElement(Imgproc.MORPH_RECT, new Size(1, lineLength)));
            Imgproc.morphologyEx(inCrop, horizontal, Imgproc.MORPH_OPEN, horizontalKernel);
            Imgproc.morphologyEx(inCrop, vertical, Imgproc.MORPH_OPEN, verticalKernel);
            Mat candidates = own(owned, new Mat());
            Core.bitwise_or(strong, horizontal, candidates);
            Core.bitwise_or(candidates, vertical, candidates);

            Mat labels = own(owned, new Mat());
            Mat stats = own(owned, new Mat());
            Mat centroids = own(owned, new Mat());
            int count = Imgproc.connectedComponentsWithStats(candidates, labels, stats, centroids, 8, CvType.CV_32S);
            byte[] clean = selectStructuralComponents(candidates, labels, stats, count, crop.width, crop.height);
            // Contours supply a second independent size check for a usable plan.
            Mat cleanMat = own(owned, new Mat(crop.height, crop.width, CvType.CV_8UC1));
            cleanMat.put(0, 0, clean);
            Mat contourCopy = own(owned, cleanMat.clone());
            Mat hierarchy = own(owned, new Mat());
            List<MatOfPoint> contours = new ArrayList<>();
            Imgproc.findContours(contourCopy, contours, hierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);
            double widestContour = 0;
            for (MatOfPoint contour : contours) {
                Rect bounds = Imgproc.boundingRect(contour);
                widestContour = Math.max(widestContour, Math.max(bounds.width, bounds.height));
                contour.release();
            }
            if (widestContour < Math.min(crop.width, crop.height) * .2)
                throw new IllegalArgumentException("No structural black wall network was found.");

            Mat distance = own(owned, new Mat());
            Imgproc.distanceTransform(cleanMat, distance, Imgproc.DIST_L2, 3);
            int wallWidth = estimateWidth(distance, clean, crop.width, crop.height);
            suppressDenseInteriorBlocks(clean, distance, crop.width, crop.height, wallWidth, owned);
            byte[] rawCrop = new byte[crop.width * crop.height];
            inCrop.get(0, 0, rawCrop);
            reinforceExteriorBottom(clean, rawCrop, crop.width, crop.height, wallWidth);
            byte[] floor = findFloor(clean, rawCrop, crop.width, crop.height);
            byte[] wallFull = new byte[width * height];
            byte[] floorFull = new byte[width * height];
            for (int y = 0; y < crop.height; y++) {
                System.arraycopy(clean, y * crop.width, wallFull, (y + crop.y) * width + crop.x, crop.width);
                System.arraycopy(floor, y * crop.width, floorFull, (y + crop.y) * width + crop.x, crop.width);
            }
            ByteBuffer output = ByteBuffer.allocate(32 + wallFull.length + floorFull.length).order(ByteOrder.LITTLE_ENDIAN);
            output.putInt(MAGIC).putInt(VERSION).putInt(crop.x).putInt(crop.y)
                  .putInt(crop.width).putInt(crop.height).putInt(wallWidth).putInt(0);
            output.put(wallFull).put(floorFull);
            return output.array();
        } finally {
            for (int i = owned.size() - 1; i >= 0; i--) owned.get(i).release();
        }
    }

    private static Mat own(List<Mat> owned, Mat mat) { owned.add(mat); return mat; }

    private static Rect detectDocumentCrop(byte[] black, int width, int height) {
        if (width < 240 || height < 300) return new Rect(0, 0, width, height);
        List<Integer> lines = new ArrayList<>();
        for (int x = 0; x < width; x++) {
            int dark = 0;
            for (int y = 0; y < height; y++) if (black[y * width + x] != 0) dark++;
            if (dark >= height * .82 && (lines.isEmpty() || x - lines.get(lines.size() - 1) > 3)) lines.add(x);
        }
        int right = width;
        // A full-height rule between the drawing and a narrow right-hand title
        // panel is a document boundary, even when its neighbouring rules are faint.
        for (int x : lines) if (x >= width * .68 && x <= width * .82) {
            right = Math.min(right, x - 2);
            break;
        }
        int left = 0;
        for (int x : lines) if (x < width * .08 && x + 3 < right) left = x + 3;
        int top = 0;
        for (int y = 1; y < height * .1; y++) {
            int dark = 0;
            for (int x = left; x < right; x++) if (black[y * width + x] != 0) dark++;
            if (dark >= (right - left) * .78) { top = y + 3; break; }
        }
        int bottom = height;
        if (right < width) for (int y = (int)(height * .88); y < height; y++) {
            int dark = 0;
            for (int x = left; x < right; x++) if (black[y * width + x] != 0) dark++;
            if (dark >= (right - left) * .5) { bottom = Math.min(height, y + 4); break; }
        }
        return right - left >= width * .45 && bottom - top >= height * .75
            ? new Rect(left, top, right - left, bottom - top)
            : new Rect(0, 0, width, height);
    }

    private static byte[] selectStructuralComponents(Mat mask, Mat labels, Mat stats, int count, int width, int height) {
        int dominant = -1, dominantBounds = 0;
        for (int i = 1; i < count; i++) {
            int area = (int) stats.get(i, Imgproc.CC_STAT_WIDTH)[0] * (int) stats.get(i, Imgproc.CC_STAT_HEIGHT)[0];
            if (area > dominantBounds) { dominantBounds = area; dominant = i; }
        }
        byte[] result = new byte[width * height];
        if (dominant < 0) return result;
        boolean[] keep = new boolean[count]; keep[dominant] = true;
        int mainLeft = (int) stats.get(dominant, Imgproc.CC_STAT_LEFT)[0];
        int mainTop = (int) stats.get(dominant, Imgproc.CC_STAT_TOP)[0];
        int mainRight = mainLeft + (int) stats.get(dominant, Imgproc.CC_STAT_WIDTH)[0];
        int mainBottom = mainTop + (int) stats.get(dominant, Imgproc.CC_STAT_HEIGHT)[0];
        int minimum = Math.min(width, height);
        int[] ids = new int[width * height]; labels.get(0, 0, ids);
        for (int i = 1; i < count; i++) {
            if (i == dominant) continue;
            int x = (int) stats.get(i, Imgproc.CC_STAT_LEFT)[0];
            int y = (int) stats.get(i, Imgproc.CC_STAT_TOP)[0];
            int w = (int) stats.get(i, Imgproc.CC_STAT_WIDTH)[0];
            int h = (int) stats.get(i, Imgproc.CC_STAT_HEIGHT)[0];
            int longSide = Math.max(w, h), shortSide = Math.min(w, h);
            boolean sizeable = longSide >= minimum * .3 && w * h >= dominantBounds * .08;
            boolean nearMain = x <= mainRight + 8 && x + w >= mainLeft - 8 && y <= mainBottom + 8 && y + h >= mainTop - 8;
            boolean connectedAtEnd = w >= h
                ? touchesDominant(ids, width, height, dominant, x, y + h / 2) || touchesDominant(ids, width, height, dominant, x + w - 1, y + h / 2)
                : touchesDominant(ids, width, height, dominant, x + w / 2, y) || touchesDominant(ids, width, height, dominant, x + w / 2, y + h - 1);
            boolean exteriorEdge = w < h
                ? Math.abs(x + w / 2 - mainLeft) <= 4 || Math.abs(x + w / 2 - mainRight) <= 4
                : Math.abs(y + h / 2 - mainTop) <= 4 || Math.abs(y + h / 2 - mainBottom) <= 4;
            boolean thinWall = longSide >= minimum * .12 && longSide >= shortSide * 3 && shortSide <= minimum * .035 && nearMain &&
                (connectedAtEnd || exteriorEdge || longSide >= minimum * .3);
            keep[i] = sizeable || thinWall;
        }
        for (int p = 0; p < ids.length; p++) if (ids[p] > 0 && keep[ids[p]]) result[p] = (byte) 255;
        return result;
    }

    private static boolean touchesDominant(int[] labels, int width, int height, int dominant, int x, int y) {
        for (int yy = Math.max(0, y - 10); yy <= Math.min(height - 1, y + 10); yy++)
            for (int xx = Math.max(0, x - 10); xx <= Math.min(width - 1, x + 10); xx++)
                if (labels[yy * width + xx] == dominant) return true;
        return false;
    }

    private static int estimateWidth(Mat distance, byte[] wall, int width, int height) {
        float[] values = new float[width * height]; distance.get(0, 0, values);
        int[] histogram = new int[17];
        for (int p = 0; p < wall.length; p++) if (wall[p] != 0) {
            int bucket = Math.min(16, Math.max(1, Math.round(values[p] * 2)));
            histogram[bucket]++;
        }
        int total = 0; for (int value : histogram) total += value;
        int cumulative = 0;
        for (int i = 1; i < histogram.length; i++) { cumulative += histogram[i]; if (cumulative >= total / 2) return Math.max(2, i); }
        return 3;
    }

    private static void suppressDenseInteriorBlocks(byte[] wall, Mat distance, int width, int height,
                                                     int thickness, List<Mat> owned) {
        Mat core = own(owned, new Mat());
        Core.inRange(distance, new Scalar(Math.max(4, thickness * 1.8)), new Scalar(100000), core);
        Mat contourCopy = own(owned, core.clone());
        Mat hierarchy = own(owned, new Mat());
        List<MatOfPoint> dense = new ArrayList<>();
        Imgproc.findContours(contourCopy, dense, hierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);
        int right = 0;
        for (int p = 0; p < wall.length; p++) if (wall[p] != 0) right = Math.max(right, p % width);
        for (MatOfPoint contour : dense) {
            Rect box = Imgproc.boundingRect(contour);
            boolean stairSized = box.width >= width * .14 && box.height >= Math.max(12, thickness * 5) &&
                box.width >= box.height * 2.2 && Imgproc.contourArea(contour) >= box.width * box.height * .4;
            if (stairSized) for (int y = Math.max(0, box.y - thickness); y < Math.min(height, box.y + box.height + thickness); y++) {
                if (y <= box.y - thickness + 2 || y >= box.y + box.height + thickness - 3) continue;
                for (int x = Math.max(0, box.x - thickness); x < Math.min(width, box.x + box.width + thickness); x++)
                    if (x < right - thickness * 2) wall[y * width + x] = 0;
            }
            contour.release();
        }
    }

    private static void reinforceExteriorBottom(byte[] wall, byte[] raw, int width, int height, int thickness) {
        int minX = width, maxX = -1, maxY = -1;
        for (int p = 0; p < wall.length; p++) if (wall[p] != 0) {
            int x = p % width, y = p / width;
            minX = Math.min(minX, x); maxX = Math.max(maxX, x); maxY = Math.max(maxY, y);
        }
        if (maxX - minX < width * .45 || maxY < height * .45) return;
        int bestY = -1, bestCount = 0, first = width, last = -1;
        for (int y = Math.max(0, maxY - 8); y <= Math.min(height - 1, maxY + 3); y++) {
            int count = 0, rowFirst = width, rowLast = -1;
            for (int x = minX; x <= maxX; x++) {
                boolean dark = false;
                for (int yy = Math.max(0, y - 1); yy <= Math.min(height - 1, y + 1); yy++)
                    if (raw[yy * width + x] != 0) { dark = true; break; }
                if (dark) { count++; rowFirst = Math.min(rowFirst, x); rowLast = x; }
            }
            if (count > bestCount) { bestY = y; bestCount = count; first = rowFirst; last = rowLast; }
        }
        if (bestY < 0 || bestCount < (maxX - minX) * .35 || last - first < (maxX - minX) * .5) return;
        for (int y = Math.max(0, bestY - thickness / 2); y <= Math.min(height - 1, bestY + thickness / 2); y++)
            for (int x = first; x <= last; x++) wall[y * width + x] = (byte)255;
    }

    private static byte[] findFloor(byte[] wall, byte[] raw, int width, int height) {
        int size = width * height;
        byte[] barrier = wall.clone();
        // Close supported collinear gaps only in the classification mask. The
        // original wall mask still contains door openings.
        int maxGap = Math.max(8, Math.min(width, height) / 4);
        for (int axis = 0; axis < 2; axis++) {
            int lines = axis == 0 ? width : height, length = axis == 0 ? height : width;
            for (int line = 0; line < lines; line++) {
                int p = 0, previousEnd = -1, previousLength = 0;
                while (p < length) {
                    while (p < length && barrier[index(axis, line, p, width)] == 0) p++;
                    if (p >= length) break;
                    int start = p;
                    while (p < length && barrier[index(axis, line, p, width)] != 0) p++;
                    int segmentLength = p - start, gap = start - previousEnd;
                    if (previousEnd >= 0 && previousLength >= 16 && segmentLength >= 16 && gap > 0 && gap <= maxGap &&
                            previousLength >= gap * .6 && segmentLength >= gap * .6)
                        for (int q = previousEnd; q < start; q++) barrier[index(axis, line, q, width)] = (byte) 255;
                    previousEnd = p; previousLength = segmentLength;
                }
            }
        }
        boolean[] exterior = new boolean[size];
        int[] queue = new int[size]; int head = 0, tail = 0;
        for (int x = 0; x < width; x++) { tail = seed(x, barrier, exterior, queue, tail); tail = seed((height - 1) * width + x, barrier, exterior, queue, tail); }
        for (int y = 0; y < height; y++) { tail = seed(y * width, barrier, exterior, queue, tail); tail = seed(y * width + width - 1, barrier, exterior, queue, tail); }
        while (head < tail) {
            int p = queue[head++], x = p % width, y = p / width;
            if (x > 0) tail = seed(p - 1, barrier, exterior, queue, tail);
            if (x + 1 < width) tail = seed(p + 1, barrier, exterior, queue, tail);
            if (y > 0) tail = seed(p - width, barrier, exterior, queue, tail);
            if (y + 1 < height) tail = seed(p + width, barrier, exterior, queue, tail);
        }
        byte[] floor = new byte[size]; int inside = 0;
        for (int p = 0; p < size; p++) if (wall[p] == 0 && !exterior[p]) { floor[p] = (byte) 255; inside++; }
        // A doorway or open parking bay can connect a real interior to the
        // exterior flood. Supplement enclosed regions with row-wise wall spans.
        // This is still an estimated footprint, not a measured room boundary.
        int minX = width, maxX = -1, minY = height, maxY = -1;
        for (int p = 0; p < size; p++) if (wall[p] != 0) {
            int x = p % width, y = p / width;
            minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y);
        }
        if (maxX <= minX || maxY <= minY) return floor;
        int stableLeft = width, stableRight = -1;
        int leftFirst = 0, leftLast = -1, rightFirst = 0, rightLast = -1;
        for (int x = minX; x <= maxX; x++) {
            int count = 0, first = height, last = -1;
            for (int y = minY; y <= maxY; y++) {
                boolean occupied = false;
                for (int xx = Math.max(minX, x - 1); xx <= Math.min(maxX, x + 1); xx++)
                    if (wall[y * width + xx] != 0) { occupied = true; break; }
                if (occupied) { count++; first = Math.min(first, y); last = y; }
            }
            if (count < height * .1 || last - first < height * .2) continue;
            if (rawNear(raw, width, height, x, maxY)) last = maxY;
            if (rawNear(raw, width, height, x, minY)) first = minY;
            if (x < stableLeft) { stableLeft = x; leftFirst = first; leftLast = last; }
            if (x > stableRight) { stableRight = x; rightFirst = first; rightLast = last; }
        }
        for (int y = minY; y <= maxY; y++) {
            int left = maxX, right = minX;
            for (int yy = Math.max(minY, y - height / 12); yy <= Math.min(maxY, y + height / 12); yy++)
                for (int x = minX; x <= maxX; x++) if (wall[yy * width + x] != 0) { left = Math.min(left, x); right = Math.max(right, x); }
            if (y >= leftFirst && y <= leftLast) left = Math.min(left, stableLeft);
            if (y >= rightFirst && y <= rightLast) right = Math.max(right, stableRight);
            for (int x = left; x <= right; x++) if (wall[y * width + x] == 0) floor[y * width + x] = (byte) 255;
        }
        return floor;
    }

    private static int index(int axis, int line, int p, int width) { return axis == 0 ? p * width + line : line * width + p; }
    private static boolean rawNear(byte[] raw, int width, int height, int x, int y) {
        for (int yy = Math.max(0, y - 8); yy <= Math.min(height - 1, y + 8); yy++)
            for (int xx = Math.max(0, x - 5); xx <= Math.min(width - 1, x + 5); xx++)
                if (raw[yy * width + xx] != 0) return true;
        return false;
    }
    private static int seed(int p, byte[] barrier, boolean[] exterior, int[] queue, int tail) {
        if (barrier[p] == 0 && !exterior[p]) { exterior[p] = true; queue[tail++] = p; }
        return tail;
    }
}
