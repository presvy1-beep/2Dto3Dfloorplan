using System;
using System.Collections.Generic;
using ARNav.Core.Data;

namespace ARNav.Core.Geometry
{
    /// <summary>Turns a serialized wall occupancy mask into ordered centerline graph paths.</summary>
    public static class WallPathExtractor
    {
        private sealed class Component
        {
            public readonly List<int> pixels = new List<int>();
            public int minX = int.MaxValue, minY = int.MaxValue, maxX, maxY, perimeter;
            public int Width { get { return maxX - minX + 1; } }
            public int Height { get { return maxY - minY + 1; } }
            public int Span { get { return Math.Max(Width, Height); } }
            public float Thickness { get { return perimeter == 0 ? 0 : pixels.Count * 2f / perimeter; } }
        }

        private sealed class TracedPath
        {
            public readonly List<int> pixels = new List<int>();
            public bool closed;
        }

        private sealed class PathSample { public float width, weight; }

        public static List<WallPathData> Extract(IList<PixelRasterRun> runs, int width, int height, out WallPathDiagnostics diagnostics)
        {
            diagnostics = new WallPathDiagnostics();
            var result = new List<WallPathData>();
            if (runs == null || runs.Count == 0 || width <= 0 || height <= 0 || (long)width * height > 32000000) return result;

            var mask = new bool[width * height];
            foreach (var run in runs)
            {
                if (run == null) continue;
                var x0 = Clamp((int)Math.Floor(run.minX), 0, width); var x1 = Clamp((int)Math.Ceiling(run.maxX), 0, width);
                var y0 = Clamp((int)Math.Floor(run.minY), 0, height); var y1 = Clamp((int)Math.Ceiling(run.maxY), 0, height);
                for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) mask[y * width + x] = true;
            }
            for (var i = 0; i < mask.Length; i++) if (mask[i]) diagnostics.rawMaskPixels++;
            var components = FindComponents(mask, width, height);
            var samples = new List<PathSample>();
            foreach (var component in components)
                if (component.Span >= 8 && component.Thickness >= 1f && component.Thickness <= Math.Min(width, height) * .06f)
                    samples.Add(new PathSample { width = component.Thickness, weight = component.Span });
            samples.Sort((a, b) => a.width.CompareTo(b.width));
            var totalWeight = 0f; foreach (var sample in samples) totalWeight += sample.weight;
            var cumulative = 0f; var strokeWidth = 1f;
            foreach (var sample in samples) { cumulative += sample.weight; if (cumulative >= totalWeight * .5f) { strokeWidth = sample.width; break; } }
            diagnostics.estimatedWidthPixels = strokeWidth;

            var skeleton = new bool[mask.Length];
            var componentIds = new int[mask.Length]; for (var i = 0; i < componentIds.Length; i++) componentIds[i] = -1;
            var eligibleIds = new List<int>();
            for (var c = 0; c < components.Count; c++)
            {
                var component = components[c];
                if (component.Span < Math.Max(4f, strokeWidth * 1.5f) || component.pixels.Count < 3) continue;
                eligibleIds.Add(c);
                var thinned = Thin(mask, width, component);
                foreach (var p in component.pixels) if (thinned[p]) { skeleton[p] = true; componentIds[p] = c; }
            }

            var minDimension = Math.Min(width, height);
            // Keep branch pruning local to the observed stroke width. Image-sized
            // cutoffs removed short but genuine partitions from tall floor plans.
            var branchLimit = Math.Max(3f, strokeWidth * 2.5f);
            branchLimit = Math.Min(branchLimit, minDimension * .015f);
            PruneShortBranches(skeleton, componentIds, width, height, components, eligibleIds, Math.Max(3, (int)Math.Round(branchLimit)));
            JoinTinyCollinearGaps(skeleton, componentIds, width, height, strokeWidth, diagnostics);
            var paths = Trace(skeleton, width, height, diagnostics);
            diagnostics.initialPaths = paths.Count;
            diagnostics.pointsBeforeSimplification = CountPoints(paths);
            var tolerance = Math.Max(1.25f, strokeWidth * 1.1f);
            var minLength = Math.Max(strokeWidth * 2.5f, 3f);
            var id = 0;
            foreach (var path in paths)
            {
                if (path.pixels.Count < 2) continue;
                var simplified = Simplify(path.pixels, width, path.closed, tolerance);
                var requiredLength = minLength;
                if (!path.closed && Degree(skeleton, width, height, path.pixels[0]) == 1 &&
                    Degree(skeleton, width, height, path.pixels[path.pixels.Count - 1]) == 1)
                    requiredLength = Math.Max(requiredLength, strokeWidth * 3f);
                if (PathLength(simplified, width, path.closed) < requiredLength) continue;
                var item = new WallPathData { id = id++, widthPixels = strokeWidth, closed = path.closed };
                foreach (var pixel in simplified) item.points.Add(new PixelPoint(pixel % width + .5f, pixel / width + .5f));
                result.Add(item);
                diagnostics.pointsAfterSimplification += item.points.Count;
            }
            diagnostics.finalPaths = result.Count;
            for (var i = 0; i < skeleton.Length; i++) if (skeleton[i]) diagnostics.skeletonPixels++;
            CountGraphNodes(skeleton, width, height, diagnostics);
            return result;
        }

        private static List<Component> FindComponents(bool[] mask, int width, int height)
        {
            var result = new List<Component>(); var seen = new bool[mask.Length]; var queue = new int[mask.Length];
            for (var seed = 0; seed < mask.Length; seed++)
            {
                if (!mask[seed] || seen[seed]) continue;
                var c = new Component(); result.Add(c); var head = 0; var tail = 0; queue[tail++] = seed; seen[seed] = true;
                while (head < tail)
                {
                    var p = queue[head++]; var x = p % width; var y = p / width; c.pixels.Add(p);
                    c.minX = Math.Min(c.minX, x); c.maxX = Math.Max(c.maxX, x); c.minY = Math.Min(c.minY, y); c.maxY = Math.Max(c.maxY, y);
                    if (x == 0 || !mask[p - 1]) c.perimeter++; if (x == width - 1 || !mask[p + 1]) c.perimeter++;
                    if (y == 0 || !mask[p - width]) c.perimeter++; if (y == height - 1 || !mask[p + width]) c.perimeter++;
                    for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue; var nx = x + ox; var ny = y + oy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        var n = ny * width + nx; if (mask[n] && !seen[n]) { seen[n] = true; queue[tail++] = n; }
                    }
                }
            }
            return result;
        }

        private static bool[] Thin(bool[] source, int width, Component c)
        {
            var lw = c.Width + 2; var lh = c.Height + 2; var local = new bool[lw * lh];
            foreach (var p in c.pixels) local[(p / width - c.minY + 1) * lw + (p % width - c.minX + 1)] = true;
            var remove = new List<int>(); var changed = true;
            while (changed)
            {
                changed = false;
                for (var phase = 0; phase < 2; phase++)
                {
                    remove.Clear();
                    for (var y = 1; y < lh - 1; y++) for (var x = 1; x < lw - 1; x++)
                    {
                        var i = y * lw + x; if (!local[i]) continue;
                        var p2=local[i-lw]; var p3=local[i-lw+1]; var p4=local[i+1]; var p5=local[i+lw+1]; var p6=local[i+lw]; var p7=local[i+lw-1]; var p8=local[i-1]; var p9=local[i-lw-1];
                        var n=(p2?1:0)+(p3?1:0)+(p4?1:0)+(p5?1:0)+(p6?1:0)+(p7?1:0)+(p8?1:0)+(p9?1:0); if (n<2||n>6) continue;
                        var t=(!p2&&p3?1:0)+(!p3&&p4?1:0)+(!p4&&p5?1:0)+(!p5&&p6?1:0)+(!p6&&p7?1:0)+(!p7&&p8?1:0)+(!p8&&p9?1:0)+(!p9&&p2?1:0); if(t!=1) continue;
                        var safe=phase==0 ? !(p2&&p4&&p6)&&!(p4&&p6&&p8) : !(p2&&p4&&p8)&&!(p2&&p6&&p8); if(safe) remove.Add(i);
                    }
                    if(remove.Count>0){foreach(var i in remove)local[i]=false;changed=true;}
                }
            }
            var result=new bool[source.Length]; for(var y=1;y<lh-1;y++)for(var x=1;x<lw-1;x++)if(local[y*lw+x])result[(c.minY+y-1)*width+c.minX+x-1]=true;
            return result;
        }

        private static void PruneShortBranches(bool[] skeleton, int[] componentIds, int width, int height, List<Component> components, List<int> ids, int maxLength)
        {
            foreach (var id in ids)
            {
                var c=components[id]; var changed=true; var guard=0;
                while(changed&&guard++<maxLength)
                {
                    changed=false; var ends=new List<int>();
                    for(var y=c.minY;y<=c.maxY;y++)for(var x=c.minX;x<=c.maxX;x++){var p=y*width+x;if(skeleton[p]&&Degree(skeleton,width,height,p)==1)ends.Add(p);}
                    foreach(var start in ends)
                    {
                        if(!skeleton[start])continue; var path=new List<int>{start}; var prev=-1; var cur=start; var junction=false;
                        while(path.Count<=maxLength){var next=Next(skeleton,width,height,cur,prev);if(next<0)break;prev=cur;cur=next;var d=Degree(skeleton,width,height,cur);if(d>=3){junction=true;break;}if(d!=2)break;path.Add(cur);}
                        if(!junction||path.Count>maxLength)continue;foreach(var p in path){skeleton[p]=false;componentIds[p]=-1;}changed=true;
                    }
                }
            }
        }

        private static void JoinTinyCollinearGaps(bool[] skeleton, int[] ids, int width, int height, float strokeWidth, WallPathDiagnostics d)
        {
            var ends=new List<int>(); for(var p=0;p<skeleton.Length;p++)if(skeleton[p]&&Degree(skeleton,width,height,p)==1)ends.Add(p);
            // Pixel centers are one cell farther apart than the number of empty
            // cells between them. Keep this bound near one estimated stroke width;
            // a doorway remains many stroke widths wider.
            var maxCenterDistance=Math.Max(3f,strokeWidth*2f+1f); var max2=maxCenterDistance*maxCenterDistance; var used=new HashSet<int>();
            var bucketSize=Math.Max(1,(int)Math.Ceiling(maxCenterDistance));var buckets=new Dictionary<long,List<int>>();
            foreach(var endpoint in ends){var bx=endpoint%width/bucketSize;var by=endpoint/width/bucketSize;var key=BucketKey(bx,by);if(!buckets.TryGetValue(key,out var bucket)){bucket=new List<int>();buckets.Add(key,bucket);}bucket.Add(endpoint);}
            foreach(var a in ends)
            {
                if(used.Contains(a)||!skeleton[a])continue;var pa=PreviousAlongEnd(skeleton,width,height,a);if(pa<0)continue;
                var ax=a%width;var ay=a/width;var avx=ax-pa%width;var avy=ay-pa/width;var best=-1;var best2=max2;var bucketX=ax/bucketSize;var bucketY=ay/bucketSize;
                for(var oy=-1;oy<=1;oy++)for(var ox=-1;ox<=1;ox++)
                {
                    if(!buckets.TryGetValue(BucketKey(bucketX+ox,bucketY+oy),out var bucket))continue;
                    foreach(var b in bucket)
                    {
                        if(b==a||used.Contains(b)||!skeleton[b])continue;var bx=b%width;var by=b/width;var dx=bx-ax;var dy=by-ay;var dist=dx*dx+dy*dy;if(dist>best2)continue;
                        var pb=PreviousAlongEnd(skeleton,width,height,b);if(pb<0)continue;var bvx=bx-pb%width;var bvy=by-pb/width;var len=(float)Math.Sqrt(dist);if(len<.01f)continue;
                        var facingA=(avx*dx+avy*dy)/(Math.Sqrt(avx*avx+avy*avy)*len);var facingB=(-bvx*dx-bvy*dy)/(Math.Sqrt(bvx*bvx+bvy*bvy)*len);
                        if(facingA<.75||facingB<.75||ids[a]==ids[b]||!BridgeIsClear(skeleton,width,height,a,b))continue;
                        best=b;best2=dist;
                    }
                }
                if(best<0)continue;var x0=ax;var y0=ay;var x1=best%width;var y1=best/width;var dxl=Math.Abs(x1-x0);var sx=x0<x1?1:-1;var dyl=-Math.Abs(y1-y0);var sy=y0<y1?1:-1;var err=dxl+dyl;
                while(true){var p=y0*width+x0;skeleton[p]=true;ids[p]=ids[a];if(x0==x1&&y0==y1)break;var e2=2*err;if(e2>=dyl){err+=dyl;x0+=sx;}if(e2<=dxl){err+=dxl;y0+=sy;}}
                used.Add(a);used.Add(best);d.joinedGaps++;
            }
        }

        private static int PreviousAlongEnd(bool[] s,int w,int h,int p){foreach(var n in Neighbors(s,w,h,p))return n;return -1;}
        private static bool BridgeIsClear(bool[] s,int w,int h,int a,int b)
        {var x=a%w;var y=a/w;var bx=b%w;var by=b/w;var dx=Math.Abs(bx-x);var sx=x<bx?1:-1;var dy=-Math.Abs(by-y);var sy=y<by?1:-1;var err=dx+dy;while(x!=bx||y!=by){var e2=2*err;if(e2>=dy){err+=dy;x+=sx;}if(e2<=dx){err+=dx;y+=sy;}if(x==bx&&y==by)break;var index=y*w+x;if(s[index])return false;for(var oy=-1;oy<=1;oy++)for(var ox=-1;ox<=1;ox++){var nx=x+ox;var ny=y+oy;if(nx<0||ny<0||nx>=w||ny>=h)continue;var p=ny*w+nx;if(s[p]&&p!=a&&p!=b)return false;}}return true;}
        private static int Degree(bool[] s,int w,int h,int p){var n=0;foreach(var _ in Neighbors(s,w,h,p))n++;return n;}
        private static List<int> Neighbors(bool[] s,int w,int h,int p)
        {var r=new List<int>(4);var x=p%w;var y=p/w;for(var oy=-1;oy<=1;oy++)for(var ox=-1;ox<=1;ox++){if(ox==0&&oy==0)continue;var nx=x+ox;var ny=y+oy;if(nx<0||ny<0||nx>=w||ny>=h)continue;var n=ny*w+nx;if(!s[n])continue;if(ox!=0&&oy!=0&&(s[y*w+nx]||s[ny*w+x]))continue;r.Add(n);}return r;}
        private static int Next(bool[] s,int w,int h,int p,int previous){foreach(var n in Neighbors(s,w,h,p))if(n!=previous)return n;return -1;}

        private static List<TracedPath> Trace(bool[] skeleton,int width,int height,WallPathDiagnostics d)
        {
            var result=new List<TracedPath>();var used=new HashSet<long>();
            for(var p=0;p<skeleton.Length;p++)if(skeleton[p]){var ns=Neighbors(skeleton,width,height,p);if(ns.Count==2)continue;foreach(var n in ns){if(used.Contains(EdgeKey(p,n)))continue;var path=new TracedPath();path.pixels.Add(p);var prev=p;var cur=n;used.Add(EdgeKey(prev,cur));path.pixels.Add(cur);var guard=0;while(guard++<skeleton.Length){var near=Neighbors(skeleton,width,height,cur);if(near.Count!=2)break;var next=near[0]==prev?near[1]:near[0];if(used.Contains(EdgeKey(cur,next)))break;used.Add(EdgeKey(cur,next));prev=cur;cur=next;path.pixels.Add(cur);}if(path.pixels.Count>=2)result.Add(path);}}
            for(var p=0;p<skeleton.Length;p++)if(skeleton[p])foreach(var n in Neighbors(skeleton,width,height,p))if(!used.Contains(EdgeKey(p,n))){var path=new TracedPath{closed=true};path.pixels.Add(p);var prev=p;var cur=n;used.Add(EdgeKey(prev,cur));var guard=0;while(cur!=p&&guard++<skeleton.Length){path.pixels.Add(cur);var near=Neighbors(skeleton,width,height,cur);var next=-1;foreach(var candidate in near)if(candidate!=prev&&!used.Contains(EdgeKey(cur,candidate))){next=candidate;break;}if(next<0)break;used.Add(EdgeKey(cur,next));prev=cur;cur=next;}if(cur==p)path.pixels.Add(p);else path.closed=false;if(path.pixels.Count>=3)result.Add(path);}
            return result;
        }

        private static long EdgeKey(int a,int b){var lo=Math.Min(a,b);var hi=Math.Max(a,b);return ((long)lo<<32)|(uint)hi;}
        private static long BucketKey(int x,int y){return ((long)x<<32)|(uint)y;}
        private static void CountGraphNodes(bool[] s,int w,int h,WallPathDiagnostics d){for(var p=0;p<s.Length;p++)if(s[p]){var n=Degree(s,w,h,p);if(n==1)d.endpoints++;else if(n>=3)d.junctions++;}}
        private static int CountPoints(List<TracedPath> paths){var n=0;foreach(var p in paths)n+=p.pixels.Count;return n;}

        private static List<int> Simplify(List<int> input,int width,bool closed,float tolerance)
        {
            if(input.Count<=2)return new List<int>(input);
            if(!closed)return DouglasPeucker(input,width,tolerance);
            var unique=new List<int>(input);if(unique[unique.Count-1]==unique[0])unique.RemoveAt(unique.Count-1);if(unique.Count<3)return unique;
            var anchor=0;var far=-1f;var x0=unique[0]%width;var y0=unique[0]/width;for(var i=1;i<unique.Count;i++){var dx=unique[i]%width-x0;var dy=unique[i]/width-y0;var q=dx*dx+dy*dy;if(q>far){far=q;anchor=i;}}
            var a=new List<int>();for(var i=0;i<=anchor;i++)a.Add(unique[i]);var b=new List<int>();for(var i=anchor;i<unique.Count;i++)b.Add(unique[i]);b.Add(unique[0]);var sa=DouglasPeucker(a,width,tolerance);var sb=DouglasPeucker(b,width,tolerance);sa.RemoveAt(sa.Count-1);sa.AddRange(sb);return sa;
        }
        private static List<int> DouglasPeucker(List<int> points,int width,float tol)
        {var keep=new bool[points.Count];keep[0]=keep[points.Count-1]=true;var stack=new Stack<int>();stack.Push(0);stack.Push(points.Count-1);var t2=tol*tol;while(stack.Count>0){var end=stack.Pop();var start=stack.Pop();var ax=points[start]%width;var ay=points[start]/width;var bx=points[end]%width;var by=points[end]/width;var dx=bx-ax;var dy=by-ay;var den=dx*dx+dy*dy;var max=t2;var index=-1;for(var i=start+1;i<end;i++){var px=points[i]%width;var py=points[i]/width;var u=den<.001?0f:(float)Math.Max(0,Math.Min(1,((px-ax)*dx+(py-ay)*dy)/(float)den));var ex=px-(ax+u*dx);var ey=py-(ay+u*dy);var q=ex*ex+ey*ey;if(q>max){max=q;index=i;}}if(index>=0){keep[index]=true;stack.Push(start);stack.Push(index);stack.Push(index);stack.Push(end);}}var result=new List<int>();for(var i=0;i<points.Count;i++)if(keep[i])result.Add(points[i]);return result;}
        private static float PathLength(List<int> p,int width,bool closed){var n=0f;for(var i=1;i<p.Count;i++){var dx=p[i]%width-p[i-1]%width;var dy=p[i]/width-p[i-1]/width;n+=(float)Math.Sqrt(dx*dx+dy*dy);}if(closed&&p.Count>2){var dx=p[0]%width-p[p.Count-1]%width;var dy=p[0]/width-p[p.Count-1]/width;n+=(float)Math.Sqrt(dx*dx+dy*dy);}return n;}
        private static int Clamp(int value,int min,int max){return value<min?min:value>max?max:value;}
    }
}
