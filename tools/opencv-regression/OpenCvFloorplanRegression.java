import com.arnav.filepicker.OpenCvFloorplanProcessor;
import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import javax.imageio.ImageIO;

/** Runs the production Java classifier against the two private sample JPEGs. */
public final class OpenCvFloorplanRegression {
    private static final class Mask {
        final byte[] bytes;
        final int width, height, cropX, cropY, cropWidth, cropHeight;
        Mask(byte[] bytes, int width, int height) {
            this.bytes = bytes; this.width = width; this.height = height;
            ByteBuffer header = ByteBuffer.wrap(bytes, 0, 32).order(ByteOrder.LITTLE_ENDIAN);
            require(header.getInt() == 0x4f435650 && header.getInt() == 1, "Invalid OpenCV result header");
            cropX = header.getInt(); cropY = header.getInt(); cropWidth = header.getInt(); cropHeight = header.getInt();
        }
        boolean wall(int x, int y) { return bytes[32 + y * width + x] != 0; }
        boolean floor(int x, int y) { return bytes[32 + width * height + y * width + x] != 0; }
        int countWalls() { int count = 0; for (int p = 0; p < width * height; p++) if (bytes[32 + p] != 0) count++; return count; }
        int countFloors() { int count = 0; for (int p = 0; p < width * height; p++) if (bytes[32 + width * height + p] != 0) count++; return count; }
    }

    public static void main(String[] paths) throws Exception {
        require(paths.length == 2, "Pass landscape and portrait JPEG paths.");
        Mask landscape = process(paths[0]);
        require(landscape.width == 640 && landscape.height == 480, "Unexpected landscape fixture dimensions.");
        require(landscape.countWalls() > 9000 && landscape.countWalls() < 15000, "Landscape wall mask changed substantially.");
        require(landscape.countFloors() > 140000, "Landscape living-room floor is incomplete.");
        require(landscape.wall(400, 416), "Landscape bottom exterior wall is missing.");
        require(landscape.floor(200, 300), "Landscape living-room floor is missing.");
        require(landscape.floor(183, 130), "Landscape furniture became a wall.");
        require(!landscape.wall(480, 205), "Landscape bathroom doorway was closed.");

        Mask portrait = process(paths[1]);
        require(portrait.width == 477 && portrait.height == 642, "Unexpected portrait fixture dimensions.");
        require(portrait.cropX >= 25 && portrait.cropX + portrait.cropWidth < 360, "Portrait title block was not cropped.");
        require(portrait.countWalls() > 10000 && portrait.countWalls() < 17000, "Portrait wall mask changed substantially.");
        require(portrait.countFloors() > 120000, "Portrait living-room floor is incomplete.");
        require(portrait.wall(60, 200), "Portrait exterior wall is missing.");
        require(portrait.floor(200, 270), "Portrait living-room floor is missing.");
        require(portrait.floor(80, 550), "Portrait car drawing made a floor hole.");
        require(!portrait.wall(290, 355), "Portrait stair hatch became a solid wall block.");
        require(!portrait.wall(400, 200), "Portrait title block became a wall.");
        System.out.println("OpenCV floorplan regressions passed (2 real plans).");
    }

    private static Mask process(String path) throws Exception {
        BufferedImage image = ImageIO.read(new File(path));
        require(image != null, "Could not read " + path);
        int width = image.getWidth(), height = image.getHeight();
        byte[] blackness = new byte[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
            int rgb = image.getRGB(x, y);
            blackness[y * width + x] = (byte)Math.max((rgb >>> 16) & 255, Math.max((rgb >>> 8) & 255, rgb & 255));
        }
        Mask mask = new Mask(OpenCvFloorplanProcessor.process(blackness, width, height), width, height);
        System.out.println(new File(path).getName() + ": crop=" + mask.cropX + "," + mask.cropY + " " + mask.cropWidth + "x" + mask.cropHeight +
            ", walls=" + mask.countWalls() + ", floor=" + mask.countFloors());
        return mask;
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
