using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using UnityEngine;

namespace ARNav.Runtime.Generation
{
    /// <summary>
    /// Strokes ordered centerline segments into one occupancy field. This field is a
    /// geometric union of the vector strokes (including joins and junctions), not the
    /// original image mask. The existing boundary mesher then emits one closed mass.
    /// </summary>
    public static class WallPathMeshBuilder
    {
        public sealed class Diagnostics
        {
            public int pathCount, inputPoints, gridWidth, gridHeight, footprintCells;
            public float cellSizeMetres, footprintAreaSquareMetres;
        }

        public static Mesh Build(IList<LocalWallPathData> paths, float baseY, float height, out Diagnostics diagnostics)
        {
            diagnostics = new Diagnostics();
            if (paths == null || paths.Count == 0 || !(height > 0)) return null;
            var minX=float.PositiveInfinity;var maxX=float.NegativeInfinity;var minZ=float.PositiveInfinity;var maxZ=float.NegativeInfinity;var minWidth=float.PositiveInfinity;
            foreach(var path in paths)
            {
                if(!Valid(path))continue;diagnostics.pathCount++;diagnostics.inputPoints+=path.points.Count;minWidth=Math.Min(minWidth,path.widthMetres);var r=path.widthMetres*.5f;
                foreach(var point in path.points){minX=Math.Min(minX,point.x-r);maxX=Math.Max(maxX,point.x+r);minZ=Math.Min(minZ,point.z-r);maxZ=Math.Max(maxZ,point.z+r);}
            }
            if(diagnostics.pathCount==0)return null;
            var cell=Math.Max(.005f,Math.Min(.02f,minWidth/3f));
            var gridWidth=Math.Max(1,(int)Math.Ceiling((maxX-minX)/cell));var gridHeight=Math.Max(1,(int)Math.Ceiling((maxZ-minZ)/cell));
            const long maxCells=2000000;
            if((long)gridWidth*gridHeight>maxCells){cell=(float)(cell*Math.Sqrt(gridWidth*(double)gridHeight/maxCells));gridWidth=Math.Max(1,(int)Math.Ceiling((maxX-minX)/cell));gridHeight=Math.Max(1,(int)Math.Ceiling((maxZ-minZ)/cell));}
            if((long)gridWidth*gridHeight>maxCells*1.1)throw new InvalidOperationException("Vector wall footprint exceeds the meshing limit.");
            diagnostics.cellSizeMetres=cell;diagnostics.gridWidth=gridWidth;diagnostics.gridHeight=gridHeight;
            var occupied=new bool[gridWidth*gridHeight];
            foreach(var path in paths)
            {
                if(!Valid(path))continue;var radius=path.widthMetres*.5f;
                for(var i=1;i<path.points.Count;i++)RasterizeSegment(path.points[i-1],path.points[i],radius,minX,minZ,cell,gridWidth,gridHeight,occupied);
                if(path.closed&&path.points.Count>2)RasterizeSegment(path.points[path.points.Count-1],path.points[0],radius,minX,minZ,cell,gridWidth,gridHeight,occupied);
                else if(path.points.Count==1)RasterizeSegment(path.points[0],path.points[0],radius,minX,minZ,cell,gridWidth,gridHeight,occupied);
            }
            for(var i=0;i<occupied.Length;i++)if(occupied[i])diagnostics.footprintCells++;
            diagnostics.footprintAreaSquareMetres=diagnostics.footprintCells*cell*cell;
            var runs=ToRuns(occupied,gridWidth,gridHeight,minX,minZ,cell);
            return ArchitecturalWallMaskMeshBuilder.Build(runs,baseY,height);
        }

        private static bool Valid(LocalWallPathData p)
        {if(p==null||p.points==null||p.points.Count<2||!(p.widthMetres>0)||float.IsNaN(p.widthMetres)||float.IsInfinity(p.widthMetres))return false;foreach(var q in p.points)if(float.IsNaN(q.x)||float.IsNaN(q.z)||float.IsInfinity(q.x)||float.IsInfinity(q.z))return false;return true;}

        private static void RasterizeSegment(LocalPoint a,LocalPoint b,float radius,float originX,float originZ,float cell,int width,int height,bool[] occupied)
        {
            var minX=Math.Max(0,(int)Math.Floor((Math.Min(a.x,b.x)-radius-originX)/cell));var maxX=Math.Min(width-1,(int)Math.Floor((Math.Max(a.x,b.x)+radius-originX)/cell));
            var minZ=Math.Max(0,(int)Math.Floor((Math.Min(a.z,b.z)-radius-originZ)/cell));var maxZ=Math.Min(height-1,(int)Math.Floor((Math.Max(a.z,b.z)+radius-originZ)/cell));
            var dx=b.x-a.x;var dz=b.z-a.z;var length2=dx*dx+dz*dz;var radius2=radius*radius;
            for(var z=minZ;z<=maxZ;z++)for(var x=minX;x<=maxX;x++)
            {
                var px=originX+(x+.5f)*cell;var pz=originZ+(z+.5f)*cell;var t=length2<1e-12f?0f:Mathf.Clamp01(((px-a.x)*dx+(pz-a.z)*dz)/length2);var ex=px-(a.x+t*dx);var ez=pz-(a.z+t*dz);
                if(ex*ex+ez*ez<=radius2)occupied[z*width+x]=true;
            }
        }

        private static List<LocalRasterRun> ToRuns(bool[] mask,int width,int height,float originX,float originZ,float cell)
        {
            var runs=new List<LocalRasterRun>();var active=new Dictionary<long,LocalRasterRun>();
            for(var z=0;z<height;z++)
            {
                var current=new Dictionary<long,LocalRasterRun>();var x=0;
                while(x<width)
                {
                    while(x<width&&!mask[z*width+x])x++;if(x>=width)break;var start=x;while(x+1<width&&mask[z*width+x+1])x++;var end=x+1;var key=((long)start<<32)|(uint)end;
                    if(active.TryGetValue(key,out var prior)&&Math.Abs(prior.maxZ-(originZ+z*cell))<cell*.001f)prior.maxZ=originZ+(z+1)*cell;
                    else{prior=new LocalRasterRun{minX=originX+start*cell,maxX=originX+end*cell,minZ=originZ+z*cell,maxZ=originZ+(z+1)*cell};runs.Add(prior);}current[key]=prior;x++;
                }
                active=current;
            }
            return runs;
        }
    }
}
