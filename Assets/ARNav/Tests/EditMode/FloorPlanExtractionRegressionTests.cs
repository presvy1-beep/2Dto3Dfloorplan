using System;
using System.Security.Cryptography;
using System.Text;
using ARNav.Core.Data;
using ARNav.Runtime.Extraction;
using NUnit.Framework;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public sealed class FloorPlanExtractionRegressionTests
    {
        [Test] public void BlankNonSquareOutputMatchesPreOptimization() => AssertFixture(Blank(97, 53), 97, 53, null, "83bdea4e96d26af7701dbb172bbefc4f6e89e0f7aa4390d7e8ee34f6ff16c07c", false, 0, 0);
        [Test] public void ColoredRoomsAndDoorwayMatchPreOptimization()
        {
            const int w = 260, h = 220; var lum = new byte[w * h]; var color = new Color32[w * h]; var page = new Color32(71,112,76,255); var floor = new Color32(255,255,255,255); var wall = new Color32(5,5,5,255);
            for (var i=0;i<lum.Length;i++){lum[i]=95;color[i]=page;} Fill(lum,color,w,24,24,236,196,floor); Line(lum,color,w,24,24,236,24,wall); Line(lum,color,w,236,24,236,196,wall); Line(lum,color,w,236,196,24,196,wall); Line(lum,color,w,24,196,24,24,wall); Line(lum,color,w,120,24,120,86,wall); Line(lum,color,w,120,104,120,196,wall); Line(lum,color,w,120,112,178,112,wall); Line(lum,color,w,196,112,236,112,wall);
            AssertFixture(lum,w,h,color,"242808f065465218b8860f6993611b8b9f1c4a2a3f37a7516a58c9fbbe06cb39",true,1,6);
        }
        [Test] public void MultiRoomGridMatchesPreOptimization()
        {
            const int w=1100,h=400;var image=Blank(w,h);for(var y=0;y<h;y++){image[y*w]=0;image[y*w+400]=0;image[y*w+700]=0;image[y*w+w-1]=0;}for(var x=0;x<w;x++){image[x]=0;image[(h-1)*w+x]=0;}
            AssertFixture(image,w,h,null,"2d714d23b115e4fc4b41201ad4172b598d6e23328a46e0d5651942a43dfebfeb",true,3,6);
        }
        [Test] public void InvalidInputReturnsFailureWithoutThrowing()
        { var result=new SimpleFloorPlanExtractor().Extract(null,default); Assert.That(result.succeeded,Is.False); }
        private static void AssertFixture(byte[] p,int w,int h,Color32[] c,string hash,bool success,int rooms,int walls)
        { var result=new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(w,h,p,c),FloorPlanCropRegion.Full(w,h)); Assert.That(result.succeeded,Is.EqualTo(success));Assert.That(result.annotations.rooms.Count,Is.EqualTo(rooms));Assert.That(result.annotations.walls.Count,Is.EqualTo(walls));using(var sha=SHA256.Create()){var actual=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(result.annotations)))).Replace("-","").ToLowerInvariant();Assert.That(actual,Is.EqualTo(hash));} }
        private static byte[] Blank(int w,int h){var p=new byte[w*h];for(var i=0;i<p.Length;i++)p[i]=255;return p;}
        private static void Fill(byte[] l,Color32[] c,int w,int x0,int y0,int x1,int y1,Color32 color){for(var y=y0;y<=y1;y++)for(var x=x0;x<=x1;x++){c[y*w+x]=color;l[y*w+x]=color.r==255?(byte)255:(byte)5;}}
        private static void Line(byte[] l,Color32[] c,int w,int x0,int y0,int x1,int y1,Color32 color){var dx=Math.Abs(x1-x0);var sx=x0<x1?1:-1;var dy=-Math.Abs(y1-y0);var sy=y0<y1?1:-1;var e=dx+dy;while(true){for(var oy=-2;oy<=2;oy++)for(var ox=-2;ox<=2;ox++){var x=x0+ox;var y=y0+oy;if(x>=0&&y>=0&&x<w&&y<220){c[y*w+x]=color;l[y*w+x]=color.r;}}if(x0==x1&&y0==y1)break;var e2=2*e;if(e2>=dy){e+=dy;x0+=sx;}if(e2<=dx){e+=dx;y0+=sy;}}}
    }
}
