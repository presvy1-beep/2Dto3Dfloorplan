import com.arnav.filepicker.OpenCvFloorplanProcessor;
import java.awt.image.BufferedImage;
import java.io.File;
import java.io.FileWriter;
import java.io.PrintWriter;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.security.MessageDigest;
import java.util.Arrays;
import java.awt.Color;
import java.awt.BasicStroke;
import java.awt.Graphics2D;
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
        require(paths.length >= 2, "Pass landscape and portrait JPEG paths.");
        syntheticRegressions();
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
        reproducibility(paths[0], paths[1], paths.length > 2 ? paths[2] : "Logs/opencv-reproducibility.csv");
        System.out.println("OpenCV floorplan regressions and reproducibility checks passed.");
    }

    private static void syntheticRegressions() throws Exception {
        System.out.println("Synthetic detector metrics (pixel precision/recall, opening preservation):");
        for (int fixture = 0; fixture < 5; fixture++) {
            BufferedImage image=new BufferedImage(256,224,BufferedImage.TYPE_BYTE_GRAY);
            Graphics2D g=image.createGraphics();g.setColor(Color.WHITE);g.fillRect(0,0,256,224);g.setColor(Color.BLACK);g.setStroke(new BasicStroke(6));
            boolean doorway=fixture==2;
            if(fixture<4){
                if(fixture==3){
                    g.drawLine(40,40,210,40);g.drawLine(40,40,40,180);g.drawLine(40,180,150,180);
                    g.drawLine(150,180,150,110);g.drawLine(150,110,210,110);g.drawLine(210,110,210,40);
                    g.drawLine(100,40,100,180);g.drawLine(150,40,150,110);
                }else{
                g.drawLine(40,40,210,40);g.drawLine(40,40,40,180);g.drawLine(210,40,210,180);
                if(!doorway)g.drawLine(40,180,210,180);else{g.drawLine(40,180,112,180);g.drawLine(146,180,210,180);}
                if(fixture==1)g.drawLine(125,40,125,180);
                if(fixture==2){} // door opening is deliberately left clear
                }
            }else{
                g.drawLine(40,40,210,40);g.drawLine(40,40,40,180);g.drawLine(210,40,210,180);g.drawLine(40,180,210,180);
                g.setStroke(new BasicStroke(2));g.drawRect(70,75,48,28);g.drawOval(145,112,35,22);g.drawString("BED ROOM 12",76,58);
            }
            g.dispose();
            int w=image.getWidth(),h=image.getHeight();byte[] gray=new byte[w*h],expected=new byte[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)gray[y*w+x]=(byte)(image.getRaster().getSample(x,y,0));
            // Expected geometry is authored before processing. Allow edge erosion/dilation by
            // scoring a one-pixel tolerance, while separate furniture/text stays forbidden.
            drawExpected(expected,w,h,fixture);
            Mask result=new Mask(OpenCvFloorplanProcessor.process(gray,w,h),w,h);
            saveMask(result,new File("Logs/synthetic-"+new String[]{"rectangle","two_rooms","doorway","l_shape","furniture_text"}[fixture]+"-opencv.png"));
            reportSynthetic(fixture,result,expected,doorway);
            if(fixture==0 || fixture==1 || fixture==2 || fixture==3)
                require(result.countWalls()>0,"Basic fixture produced no wall output: "+fixture);
        }
    }
    private static void drawExpected(byte[] expected,int w,int h,int fixture){
        // The structural target uses the same intended 6px wall thickness, drawn as pixels.
        if(fixture==3){expectedLine(expected,w,40,40,210,40,6);expectedLine(expected,w,40,40,40,180,6);expectedLine(expected,w,40,180,150,180,6);expectedLine(expected,w,150,180,150,110,6);expectedLine(expected,w,150,110,210,110,6);expectedLine(expected,w,210,110,210,40,6);expectedLine(expected,w,100,40,100,180,6);expectedLine(expected,w,150,40,150,110,6);return;}
        expectedLine(expected,w,40,40,210,40,6);expectedLine(expected,w,40,40,40,180,6);expectedLine(expected,w,210,40,210,180,6);
        if(fixture==2){expectedLine(expected,w,40,180,112,180,6);expectedLine(expected,w,146,180,210,180,6);}
        else expectedLine(expected,w,40,180,210,180,6);
        if(fixture==1)expectedLine(expected,w,125,40,125,180,6);
        if(fixture==3){expectedLine(expected,w,40,100,150,100,6);expectedLine(expected,w,150,100,150,180,6);}
    }
    private static void expectedLine(byte[] m,int w,int x0,int y0,int x1,int y1,int t){
        int steps=Math.max(Math.abs(x1-x0),Math.abs(y1-y0));for(int i=0;i<=steps;i++){int x=x0+(x1-x0)*i/steps,y=y0+(y1-y0)*i/steps;for(int oy=-t/2;oy<=t/2;oy++)for(int ox=-t/2;ox<=t/2;ox++)if(x+ox>=0&&x+ox<w&&y+oy>=0&&y+oy<m.length/w)m[(y+oy)*w+x+ox]=(byte)255;}
    }
    private static void reportSynthetic(int fixture,Mask actual,byte[] expected,boolean doorway){
        int w=actual.width,h=actual.height,tp=0,fn=0,fp=0;for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){
            boolean target=false,detected=false;
            for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){target|=expected[(y+oy)*w+x+ox]!=0;detected|=actual.wall(x+ox,y+oy);}
            if(target&&detected)tp++;else if(target)fn++;else if(actual.wall(x,y))fp++;
        }
        double recall=tp+fn==0?1:(double)tp/(tp+fn),precision=tp+fp==0?1:(double)tp/(tp+fp);
        boolean openingKept=!doorway||!actual.wall(129,180);
        System.out.println("fixture="+new String[]{"rectangle","two_rooms","doorway","L_shape","furniture_text"}[fixture]+" precision="+String.format("%.3f",precision)+" recall="+String.format("%.3f",recall)+" falsePixels="+fp+" missingPixels="+fn+" openingPreserved="+openingKept);
    }

    private static void reproducibility(String aPath, String bPath, String reportPath) throws Exception {
        Input a = load(aPath), b = load(bPath);
        Snapshot first = snapshot(a);
        File report=new File(reportPath);File parent=report.getParentFile();if(parent!=null)parent.mkdirs();
        File nativeDir=new File(report.getParentFile()==null?new File("Logs"):report.getParentFile(),"opencv-native-runs");nativeDir.mkdirs();
        try(PrintWriter out=new PrintWriter(new FileWriter(report))) {
            out.println("sequence,run,width,height,input_sha256,wall_mask_sha256,wall_run_sha256,floor_run_sha256,native_result_sha256,identical_to_first");
            byte[] baselineNative=OpenCvFloorplanProcessor.process(a.pixels,a.width,a.height);
            java.nio.file.Files.write(new File(nativeDir,"A_baseline.bin").toPath(),baselineNative);
            out.println(csv("baseline",0,a,first,true,baselineNative));
            for (int run = 1; run <= 20; run++) {
                byte[] nativeResult=OpenCvFloorplanProcessor.process(a.pixels,a.width,a.height);
                Snapshot current = snapshot(a,nativeResult); boolean same=first.equals(current);
                System.out.println(String.format("run %02d input=%s walls=%s runs=%s floorRuns=%s identical=%s", run,
                    current.inputHash, current.wallHash, current.pathHash, current.floorHash, same));
                java.nio.file.Files.write(new File(nativeDir,String.format("A_repeat_%02d.bin",run)).toPath(),nativeResult);
                out.println(csv("repeat",run,a,current,same,nativeResult));
                require(same, "A output changed on consecutive run " + run);
            }
            Snapshot aBefore = snapshot(a); Snapshot bMiddle = snapshot(b); Snapshot aAfter = snapshot(a);
            byte[] bNative=OpenCvFloorplanProcessor.process(b.pixels,b.width,b.height);
            byte[] afterNative=OpenCvFloorplanProcessor.process(a.pixels,a.width,a.height);
            java.nio.file.Files.write(new File(nativeDir,"B.bin").toPath(),bNative);
            java.nio.file.Files.write(new File(nativeDir,"A_after_B.bin").toPath(),afterNative);
            boolean sameAfter=aBefore.equals(aAfter);
            out.println(csv("A_before",1,a,aBefore,true,baselineNative));out.println(csv("B",1,b,bMiddle,false,bNative));out.println(csv("A_after_B",1,a,aAfter,sameAfter,afterNative));
            System.out.println("A->B->A: A-before=" + aBefore + " B=" + bMiddle + " A-after=" + aAfter + " A-identical=" + sameAfter);
            require(sameAfter, "A result changed after processing B");
        }
        System.out.println("Saved benchmark records: " + report.getAbsolutePath());
    }
    private static String csv(String sequence,int run,Input input,Snapshot s,boolean same,byte[] nativeResult) throws Exception {
        return sequence+","+run+","+input.width+","+input.height+","+s.inputHash+","+s.wallHash+","+s.pathHash+","+s.floorHash+","+sha(nativeResult)+","+same;
    }

    private static final class Input {
        final String path; final int width, height; final byte[] pixels; final String hash;
        Input(String path, int width, int height, byte[] pixels) throws Exception {
            this.path = path; this.width = width; this.height = height; this.pixels = pixels; hash = sha(pixels);
        }
    }
    private static final class Snapshot {
        final String inputHash, wallHash, pathHash, floorHash;
        Snapshot(String inputHash, String wallHash, String pathHash, String floorHash) {
            this.inputHash=inputHash; this.wallHash=wallHash; this.pathHash=pathHash; this.floorHash=floorHash;
        }
        @Override public boolean equals(Object other) {
            if (!(other instanceof Snapshot)) return false;
            Snapshot s=(Snapshot)other;
            return inputHash.equals(s.inputHash)&&wallHash.equals(s.wallHash)&&pathHash.equals(s.pathHash)&&floorHash.equals(s.floorHash);
        }
        @Override public int hashCode() { return inputHash.hashCode() ^ wallHash.hashCode() ^ pathHash.hashCode() ^ floorHash.hashCode(); }
        @Override public String toString() { return "input="+inputHash+" walls="+wallHash+" paths="+pathHash+" floor="+floorHash; }
    }
    private static Input load(String path) throws Exception {
        BufferedImage image=ImageIO.read(new File(path)); require(image!=null,"Could not read "+path);
        byte[] pixels=new byte[image.getWidth()*image.getHeight()];
        for(int y=0;y<image.getHeight();y++)for(int x=0;x<image.getWidth();x++){
            int rgb=image.getRGB(x,y);pixels[y*image.getWidth()+x]=(byte)Math.max((rgb>>>16)&255,Math.max((rgb>>>8)&255,rgb&255));
        }
        return new Input(path,image.getWidth(),image.getHeight(),pixels);
    }
    private static Snapshot snapshot(Input input) throws Exception {
        return snapshot(input,OpenCvFloorplanProcessor.process(input.pixels,input.width,input.height));
    }
    private static Snapshot snapshot(Input input,byte[] result) throws Exception {
        int length=input.width*input.height;
        byte[] walls=Arrays.copyOfRange(result,32,32+length);
        byte[] floors=Arrays.copyOfRange(result,32+length,32+2*length);
        return new Snapshot(input.hash,sha(walls),sha(canonicalRuns(walls,input.width,input.height)),sha(canonicalRuns(floors,input.width,input.height)));
    }
    private static byte[] canonicalRuns(byte[] mask,int width,int height) {
        StringBuilder value=new StringBuilder();
        for(int y=0;y<height;y++) { int x=0; while(x<width) { while(x<width&&mask[y*width+x]==0)x++; if(x==width)break; int start=x; while(x<width&&mask[y*width+x]!=0)x++; value.append(y).append(':').append(start).append('-').append(x).append(';'); } }
        return value.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
    }
    private static String sha(byte[] bytes) throws Exception {
        byte[] digest=MessageDigest.getInstance("SHA-256").digest(bytes);StringBuilder out=new StringBuilder();for(byte b:digest)out.append(String.format("%02x",b&255));return out.toString();
    }

    private static Mask process(String path) throws Exception {
        Input input=load(path); int width=input.width,height=input.height;
        Mask mask = new Mask(OpenCvFloorplanProcessor.process(input.pixels, width, height), width, height);
        saveMask(mask,new File("Logs",new File(path).getName()+"-opencv-wall-mask.png"));
        System.out.println(new File(path).getName() + ": crop=" + mask.cropX + "," + mask.cropY + " " + mask.cropWidth + "x" + mask.cropHeight +
            ", walls=" + mask.countWalls() + ", floor=" + mask.countFloors());
        return mask;
    }

    private static void saveMask(Mask mask,File destination) throws Exception {
        BufferedImage image=new BufferedImage(mask.width,mask.height,BufferedImage.TYPE_BYTE_GRAY);
        for(int y=0;y<mask.height;y++)for(int x=0;x<mask.width;x++)image.getRaster().setSample(x,y,0,mask.wall(x,y)?0:255);
        ImageIO.write(image,"png",destination);
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
