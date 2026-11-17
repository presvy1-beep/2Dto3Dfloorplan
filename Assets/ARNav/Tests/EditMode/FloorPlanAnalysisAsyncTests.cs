using System.Collections;
using System.Reflection;
using System.Threading;
using ARNav.Runtime.Extraction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ARNav.Tests.EditMode
{
    public sealed class FloorPlanAnalysisAsyncTests
    {
        [UnityTest] public IEnumerator SuccessfulAnalysisCompletesOnUnityThread()
        {
            var owner=new GameObject("analysis-test");var controller=owner.AddComponent<FloorPlanAnalysisController>();var texture=BlankTexture(97,53);var mainThread=Thread.CurrentThread.ManagedThreadId;FloorPlanExtractionResult result=null;var callbackThread=0;
            controller.Analyze(texture,97,53,"session","hash",12,r=>{result=r;callbackThread=Thread.CurrentThread.ManagedThreadId;});
            Assert.That(controller.IsAnalyzing,Is.True);
            for(var i=0;i<300&&result==null;i++){yield return new WaitForSecondsRealtime(.01f);Pump(controller);}
            Assert.That(result,Is.Not.Null);Assert.That(result.analyzedRegion.width,Is.EqualTo(97));Assert.That(callbackThread,Is.EqualTo(mainThread));
            Object.DestroyImmediate(texture);Object.DestroyImmediate(owner);
        }
        [Test] public void CancellationInvalidatesActiveResult()
        { var owner=new GameObject("analysis-cancel-test");var controller=owner.AddComponent<FloorPlanAnalysisController>();var texture=BlankTexture(64,64);FloorPlanExtractionResult result=null;controller.Analyze(texture,64,64,r=>result=r);controller.CancelAnalysis();Assert.That(controller.IsAnalyzing,Is.False);Assert.That(result,Is.Not.Null);StringAssert.Contains("cancelled",result.message.ToLowerInvariant());Object.DestroyImmediate(texture);Object.DestroyImmediate(owner); }
        [UnityTest] public IEnumerator RepeatedRequestOnlyDeliversNewestResult()
        {
            var owner=new GameObject("analysis-repeat-test");var controller=owner.AddComponent<FloorPlanAnalysisController>();var texture=BlankTexture(64,64);var oldCalls=0;FloorPlanExtractionResult latest=null;
            controller.Analyze(texture,64,64,r=>oldCalls++);controller.Analyze(texture,64,64,"new-session","new-hash",1,r=>latest=r);
            for(var i=0;i<300&&latest==null;i++){yield return new WaitForSecondsRealtime(.01f);Pump(controller);}
            Assert.That(latest,Is.Not.Null);Assert.That(latest.sessionId,Is.EqualTo("new-session"));Assert.That(oldCalls,Is.Zero);
            Object.DestroyImmediate(texture);Object.DestroyImmediate(owner);
        }
        [Test] public void InvalidInputFailsWithoutStartingAWorker()
        { var owner=new GameObject("analysis-invalid-test");var controller=owner.AddComponent<FloorPlanAnalysisController>();FloorPlanExtractionResult result=null;controller.Analyze(null,0,0,r=>result=r);Assert.That(result,Is.Not.Null);Assert.That(result.succeeded,Is.False);Assert.That(controller.IsAnalyzing,Is.False);Object.DestroyImmediate(owner); }
        [UnityTest] public IEnumerator OwnerDisposalDoesNotDeliverObsoleteResult()
        { var owner=new GameObject("analysis-dispose-test");var controller=owner.AddComponent<FloorPlanAnalysisController>();var texture=BlankTexture(900,900);var calls=0;controller.Analyze(texture,900,900,r=>calls++);Object.DestroyImmediate(owner);for(var i=0;i<120;i++)yield return null;Assert.That(calls,Is.Zero);Object.DestroyImmediate(texture); }
        [UnityTest] public IEnumerator SupportedLargeImageAnalysisCompletes()
        { var owner=new GameObject("analysis-large-test");var controller=owner.AddComponent<FloorPlanAnalysisController>();var texture=BlankTexture(1200,900);FloorPlanExtractionResult result=null;controller.Analyze(texture,1200,900,r=>result=r);for(var i=0;i<300&&result==null;i++){yield return new WaitForSecondsRealtime(.01f);Pump(controller);}Assert.That(result,Is.Not.Null);Object.DestroyImmediate(texture);Object.DestroyImmediate(owner); }
        private static Texture2D BlankTexture(int width,int height){var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);var pixels=new Color32[width*height];for(var i=0;i<pixels.Length;i++)pixels[i]=new Color32(255,255,255,255);texture.SetPixels32(pixels);texture.Apply(false,false);return texture;}
        private static void Pump(FloorPlanAnalysisController controller){typeof(FloorPlanAnalysisController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);}
    }
}
